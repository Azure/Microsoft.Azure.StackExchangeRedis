using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Web;
using System.Web.Configuration;
using System.Web.SessionState;
using Azure.Identity;
using StackExchange.Redis;
using Newtonsoft.Json;

#pragma warning disable CS8600, CS8601, CS8603, CS8604, CS8618, CS8625

namespace RedisSessionApp
{
    /// <summary>
    /// Custom session state provider using Azure Redis Cache with distributed locking and JSON serialization.
    /// Implements SessionStateStoreProviderBase for ASP.NET Framework 4.8.
    /// </summary>
    public class RedisSessionState : SessionStateStoreProviderBase
    {
        private static ConnectionMultiplexer _connection;
        private static IDatabase _database;
        private string _applicationName;
        private int _timeout;
        private const int LOCK_TIMEOUT_SECONDS = 30;
        private const int LOCK_RETRY_MAX = 10;
        private const int LOCK_RETRY_DELAY_MS = 100;

        /// <summary>
        /// Initializes the provider. Expects 'connectionStringName' and optionally 'applicationName' in config.
        /// </summary>
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "RedisSessionState";

            base.Initialize(name, config);

            // Get connection string
            string connectionStringName = config["connectionStringName"];
            if (string.IsNullOrEmpty(connectionStringName))
                throw new ConfigurationErrorsException("connectionStringName is required");

            var connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionStringName];
            string connectionString = connectionStringSettings != null ? connectionStringSettings.ConnectionString : null;
            if (string.IsNullOrEmpty(connectionString))
                throw new ConfigurationErrorsException(string.Format("Connection string '{0}' not found", connectionStringName));

            // Get application name
            _applicationName = config["applicationName"] ?? "ASP.NET_SessionState";

            // Initialize Redis connection
            if (_connection == null)
            {
                var configurationOptions = ConfigurationOptions.Parse(connectionString);
                configurationOptions = configurationOptions.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential()).GetAwaiter().GetResult();
                _connection = ConnectionMultiplexer.Connect(configurationOptions);
                _database = _connection.GetDatabase();
            }

            // Get timeout from web.config
            var sessionStateConfig = (SessionStateSection)WebConfigurationManager.GetSection("system.web/sessionState");
            _timeout = (int)sessionStateConfig.Timeout.TotalMinutes;
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context), timeout);
        }

        /// <summary>
        /// Creates an uninitialized session (used for cookieless sessions).
        /// </summary>
        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var item = new SessionStateItem
            {
                Data = new SessionStateItemCollection(),
                Flag = SessionStateActions.InitializeItem,
                LockId = 0,
                Timeout = timeout
            };

            string key = GetRedisKey(id);
            string serializedData = SerializeSessionItem(item);
            _database.StringSet(key, serializedData, TimeSpan.FromMinutes(timeout));
        }

        /// <summary>
        /// Retrieves session data without locking (for read-only access with EnableSessionState="ReadOnly").
        /// </summary>
        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked,
            out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// Retrieves session data with exclusive distributed lock (default behavior for read-write access).
        /// </summary>
        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked,
            out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        /// <summary>
        /// Retrieves session data with optional distributed locking using Redis SETNX pattern.
        /// Lock uses unique GUID with 30-second TTL. Retries up to 10 times with exponential backoff.
        /// </summary>
        private SessionStateStoreData GetSessionStoreItem(bool lockRecord, HttpContext context, string id,
            out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            locked = false;
            lockAge = TimeSpan.Zero;
            lockId = null;
            actions = SessionStateActions.None;

            string key = GetRedisKey(id);
            string lockKey = GetLockKey(id);

            // Try to retrieve the session data
            string serializedData = _database.StringGet(key);

            if (string.IsNullOrEmpty(serializedData))
                return null;

            SessionStateItem item = DeserializeSessionItem(serializedData);

            if (item == null)
                return null;

            actions = item.Flag;

            // If we need to lock the record for exclusive access
            if (lockRecord)
            {
                // Check if session is already locked
                string existingLock = _database.StringGet(lockKey);

                if (!string.IsNullOrEmpty(existingLock))
                {
                    // Session is locked by another request
                    locked = true;
                    lockAge = DateTime.UtcNow - item.LockDate;
                    lockId = existingLock;
                    return null;
                }

                // Try to acquire the lock with retry mechanism
                string newLockId = Guid.NewGuid().ToString();
                bool lockAcquired = false;

                for (int retry = 0; retry < LOCK_RETRY_MAX && !lockAcquired; retry++)
                {
                    // Use SETNX pattern: SET if Not eXists with expiration
                    lockAcquired = _database.StringSet(lockKey, newLockId, 
                        TimeSpan.FromSeconds(LOCK_TIMEOUT_SECONDS), 
                        When.NotExists);

                    if (!lockAcquired && retry < LOCK_RETRY_MAX - 1)
                    {
                        // Wait before retrying (exponential backoff)
                        System.Threading.Thread.Sleep(LOCK_RETRY_DELAY_MS * (retry + 1));
                    }
                }

                if (!lockAcquired)
                {
                    // Could not acquire lock after retries
                    locked = true;
                    lockAge = DateTime.UtcNow - item.LockDate;
                    lockId = existingLock;
                    return null;
                }

                // Lock acquired successfully
                item.LockId = newLockId.GetHashCode();
                item.LockDate = DateTime.UtcNow;
                lockId = newLockId;

                // Update the session item with new lock info
                string updatedData = SerializeSessionItem(item);
                _database.StringSet(key, updatedData, TimeSpan.FromMinutes(item.Timeout));
            }

            return new SessionStateStoreData(item.Data,
                SessionStateUtility.GetSessionStaticObjects(context), item.Timeout);
        }

        /// <summary>
        /// Releases lock using Lua script to atomically verify ownership before deletion.
        /// </summary>
        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            if (lockId == null)
                return;

            string lockKey = GetLockKey(id);
            string lockValue = lockId.ToString();

            // Use Lua script to atomically check and delete the lock
            // Only delete if the lock value matches (we own the lock)
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end
            ";

            try
            {
                _database.ScriptEvaluate(script, new RedisKey[] { lockKey }, new RedisValue[] { lockValue });
            }
            catch
            {
                // Lock may have already expired or been released
                // This is acceptable - the lock is gone either way
            }
        }

        public override void RemoveItem(HttpContext context, string id, object lockId,
            SessionStateStoreData item)
        {
            string key = GetRedisKey(id);
            string lockKey = GetLockKey(id);

            // Delete both the session data and the lock
            _database.KeyDelete(new RedisKey[] { key, lockKey });
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            string key = GetRedisKey(id);
            _database.KeyExpire(key, TimeSpan.FromMinutes(_timeout));
        }

        /// <summary>
        /// Saves session data to Redis and releases the distributed lock.
        /// </summary>
        public override void SetAndReleaseItemExclusive(HttpContext context, string id, 
            SessionStateStoreData item, object lockId, bool newItem)
        {
            string key = GetRedisKey(id);

            var sessionItem = new SessionStateItem
            {
                Data = (SessionStateItemCollection)item.Items,
                Flag = SessionStateActions.None,
                LockId = lockId != null ? lockId.GetHashCode() : 0,
                Timeout = item.Timeout,
                LockDate = DateTime.UtcNow
            };

            // Save the session data with expiration
            string serializedData = SerializeSessionItem(sessionItem);
            _database.StringSet(key, serializedData, TimeSpan.FromMinutes(item.Timeout));

            // Release the lock
            ReleaseItemExclusive(context, id, lockId);
        }

        /// <summary>
        /// Returns false - session expiration callbacks require Redis keyspace notifications.
        /// </summary>
        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false; // Redis doesn't support callbacks for expired keys in this implementation
        }

        public override void Dispose()
        {
            if (_connection != null)
                _connection.Dispose();
        }

        public override void InitializeRequest(HttpContext context)
        {
            // No initialization needed per request
        }

        public override void EndRequest(HttpContext context)
        {
            // No cleanup needed per request
        }

        private string GetRedisKey(string sessionId)
        {
            return string.Format("{0}:session:{1}", _applicationName, sessionId);
        }

        private string GetLockKey(string sessionId)
        {
            return string.Format("{0}:session:{1}:lock", _applicationName, sessionId);
        }

        /// <summary>
        /// Serializes session to JSON. Converts SessionStateItemCollection to Dictionary since it's not JSON-serializable.
        /// </summary>
        private string SerializeSessionItem(SessionStateItem item)
        {
            try
            {
                // Convert SessionStateItemCollection to Dictionary for JSON serialization
                var sessionData = new Dictionary<string, object>();
                foreach (string key in item.Data.Keys)
                {
                    sessionData[key] = item.Data[key];
                }

                // Create a JSON-friendly wrapper object
                var jsonWrapper = new SessionStateJsonWrapper
                {
                    SessionData = sessionData,
                    Flag = (int)item.Flag,
                    LockId = item.LockId,
                    LockDate = item.LockDate,
                    Timeout = item.Timeout
                };

                return JsonConvert.SerializeObject(jsonWrapper, Formatting.None);
            }
            catch
            {
                return string.Empty;
            }
        }

        private SessionStateItem DeserializeSessionItem(string serializedData)
        {
            try
            {
                var jsonWrapper = JsonConvert.DeserializeObject<SessionStateJsonWrapper>(serializedData);

                if (jsonWrapper == null)
                    return null;

                // Convert Dictionary back to SessionStateItemCollection
                var sessionCollection = new SessionStateItemCollection();
                if (jsonWrapper.SessionData != null)
                {
                    foreach (var kvp in jsonWrapper.SessionData)
                    {
                        sessionCollection[kvp.Key] = kvp.Value;
                    }
                }

                return new SessionStateItem
                {
                    Data = sessionCollection,
                    Flag = (SessionStateActions)jsonWrapper.Flag,
                    LockId = jsonWrapper.LockId,
                    LockDate = jsonWrapper.LockDate,
                    Timeout = jsonWrapper.Timeout
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// JSON-serializable wrapper for session data. Bridges SessionStateItemCollection (not JSON-serializable) to Redis.
    /// </summary>
    internal class SessionStateJsonWrapper
    {
        public Dictionary<string, object> SessionData { get; set; }
        public int Flag { get; set; }
        public int LockId { get; set; }
        public DateTime LockDate { get; set; }
        public int Timeout { get; set; }
    }

    /// <summary>
    /// ASP.NET-facing session state wrapper. Uses JSON serialization via SessionStateJsonWrapper.
    /// </summary>
    public class SessionStateItem
    {
        public SessionStateItemCollection Data { get; set; }
        public SessionStateActions Flag { get; set; }
        public int LockId { get; set; }
        public DateTime LockDate { get; set; }
        public int Timeout { get; set; }

        public SessionStateItem()
        {
            Data = new SessionStateItemCollection();
            Flag = SessionStateActions.None;
            LockId = 0;
            LockDate = DateTime.UtcNow;
            Timeout = 20;
        }
    }
}