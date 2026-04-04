<%@ Application Language="C#" %>

<script runat="server">

    void Application_Start(object sender, EventArgs e) 
    {
        // Code that runs on application startup
        System.Diagnostics.Debug.WriteLine("Application started with Redis Session State Provider");
    }

    void Application_End(object sender, EventArgs e) 
    {
        //  Code that runs on application shutdown
    }

    void Application_Error(object sender, EventArgs e) 
    {
        // Code that runs when an unhandled error occurs
        Exception ex = Server.GetLastError();
        System.Diagnostics.Debug.WriteLine(String.Format("Application error: {0}", ex.Message));
    }

    void Session_Start(object sender, EventArgs e) 
    {
        // Code that runs when a new session is started
        System.Diagnostics.Debug.WriteLine(String.Format("New session started: {0}", Session.SessionID));
    }

    void Session_End(object sender, EventArgs e) 
    {
        // Code that runs when a session ends. 
        // Note: The Session_End event is raised only when the sessionstate mode
        // is set to InProc. If session mode is StateServer or SQLServer, 
        // the event is not raised.
        System.Diagnostics.Debug.WriteLine("Session ended");
    }
       
</script>