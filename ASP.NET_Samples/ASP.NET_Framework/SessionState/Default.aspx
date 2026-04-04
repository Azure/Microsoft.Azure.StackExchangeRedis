<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="RedisSessionApp.Default" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Redis Session State Demo - ASP.NET Framework 4.8</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 20px;
            background-color: #f5f5f5;
        }
        .container {
            max-width: 800px;
            margin: 0 auto;
            background-color: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        .header {
            background-color: #0066cc;
            color: white;
            padding: 20px;
            margin: -30px -30px 30px -30px;
            border-radius: 8px 8px 0 0;
        }
        h1 {
            margin: 0;
            font-size: 24px;
        }
        .section {
            margin: 20px 0;
            padding: 15px;
            border: 1px solid #ddd;
            border-radius: 5px;
            background-color: #f9f9f9;
        }
        .button {
            background-color: #0066cc;
            color: white;
            padding: 10px 20px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            margin: 5px;
            font-size: 14px;
        }
        .button:hover {
            background-color: #004499;
        }
        .info {
            background-color: #e7f3ff;
            border-left: 4px solid #0066cc;
            padding: 10px;
            margin: 10px 0;
        }
        .textbox {
            padding: 8px;
            border: 1px solid #ddd;
            border-radius: 4px;
            font-size: 14px;
            margin: 5px;
            width: 200px;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>Redis Session State Demo</h1>
            <p>ASP.NET Framework 4.8 with StackExchange.Redis</p>
        </div>

        <form id="form1" runat="server">
            <div class="info">
                <strong>Session ID:</strong> <%= Session.SessionID %><br />
                <strong>Session Timeout:</strong> <%= Session.Timeout %> minutes<br />
                <strong>Current Time:</strong> <%= DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") %>
            </div>

            <div class="section">
                <h3>Session Data Management</h3>
                <p>Add or update values in your session state stored in Azure Redis:</p>
                
                <asp:TextBox ID="txtKey" runat="server" CssClass="textbox" placeholder="Key"></asp:TextBox>
                <asp:TextBox ID="txtValue" runat="server" CssClass="textbox" placeholder="Value"></asp:TextBox>
                <asp:Button ID="btnAddUpdate" runat="server" Text="Add/Update" OnClick="btnAddUpdate_Click" CssClass="button" />
                
                <br /><br />
                
                <asp:TextBox ID="txtKeyToRemove" runat="server" CssClass="textbox" placeholder="Key to remove"></asp:TextBox>
                <asp:Button ID="btnRemove" runat="server" Text="Remove" OnClick="btnRemove_Click" CssClass="button" />
                
                <br /><br />
                
                <asp:Button ID="btnClear" runat="server" Text="Clear All Session Data" OnClick="btnClear_Click" CssClass="button" />
                <asp:Button ID="btnRefresh" runat="server" Text="Refresh Page" OnClick="btnRefresh_Click" CssClass="button" />
            </div>

            <div class="section">
                <h3>Current Session Contents</h3>
                <asp:Literal ID="litSessionContents" runat="server"></asp:Literal>
            </div>

            <div class="section">
                <h3>Visit Counter Demo</h3>
                <p>This counter is stored in session state and will persist across page refreshes:</p>
                <div class="info">
                    <strong>Visit Count:</strong> <asp:Label ID="lblVisitCount" runat="server"></asp:Label>
                </div>
                <asp:Button ID="btnIncrementVisits" runat="server" Text="Increment Counter" OnClick="btnIncrementVisits_Click" CssClass="button" />
                <asp:Button ID="btnResetVisits" runat="server" Text="Reset Counter" OnClick="btnResetVisits_Click" CssClass="button" />
            </div>

            <div class="section">
                <h3>Testing Instructions</h3>
                <ol>
                    <li><strong>Redis Setup:</strong> Update the connection string in Web.config to point to your Azure Redis Cache</li>
                    <li><strong>Session Persistence:</strong> Add some session data and refresh the page to see it persist</li>
                    <li><strong>Multiple Tabs:</strong> Open this page in multiple tabs and verify session data is shared</li>
                    <li><strong>Cross-Browser:</strong> Test in different browsers to see separate sessions</li>
                    <li><strong>Session Timeout:</strong> Wait 20 minutes (or configured timeout) to see session expiration</li>
                </ol>
            </div>
        </form>
    </div>
</body>
</html>