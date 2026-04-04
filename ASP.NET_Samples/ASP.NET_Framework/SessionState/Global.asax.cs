using System;

namespace RedisSessionApp
{
    public class Global : System.Web.HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            // Code that runs on application startup
            System.Diagnostics.Debug.WriteLine("Redis Session State Application started");
        }

        protected void Session_Start(object sender, EventArgs e)
        {
            // Code that runs when a new session is started
            System.Diagnostics.Debug.WriteLine(string.Format("New Redis session started: {0}", Session.SessionID));
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            // Code that runs on each request
        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {
            // Code that runs when authenticating requests
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            // Code that runs when an unhandled error occurs
            Exception ex = Server.GetLastError();
            string errorMessage = ex != null ? ex.Message : "Unknown error";
            System.Diagnostics.Debug.WriteLine(string.Format("Application error: {0}", errorMessage));
        }

        protected void Session_End(object sender, EventArgs e)
        {
            // Code that runs when a session ends
            // Note: This may not fire with custom session state providers
            System.Diagnostics.Debug.WriteLine("Redis session ended");
        }

        protected void Application_End(object sender, EventArgs e)
        {
            //  Code that runs on application shutdown
            System.Diagnostics.Debug.WriteLine("Redis Session State Application ended");
        }
    }
}