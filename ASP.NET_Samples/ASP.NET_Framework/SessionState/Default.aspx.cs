using System;
using System.Text;
using System.Web.UI;

namespace RedisSessionApp
{
    public partial class Default : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                InitializeSession();
            }
            
            UpdateDisplays();
        }

        private void InitializeSession()
        {
            // Initialize visit counter if it doesn't exist
            if (Session["VisitCount"] == null)
            {
                Session["VisitCount"] = 1;
            }
            else
            {
                int visitCount;
                if (!Int32.TryParse(Session["VisitCount"].ToString(), out visitCount))
                {
                    visitCount = 0;
                }
                Session["VisitCount"] = visitCount + 1;
            }

            // Add some sample data if session is new
            if (Session["UserName"] == null)
            {
                Session["UserName"] = "SampleUser";
                Session["LastLogin"] = DateTime.Now;
                Session["Theme"] = "Default";
            }
        }

        private void UpdateDisplays()
        {
            // Update visit count display
            lblVisitCount.Text = Session["VisitCount"]?.ToString() ?? "0";

            // Build session contents display
            var sb = new StringBuilder();
            if (Session.Count == 0)
            {
                sb.Append("<p><em>No session data found.</em></p>");
            }
            else
            {
                sb.Append("<table style='width:100%; border-collapse: collapse;'>");
                sb.Append("<tr style='background-color: #0066cc; color: white;'>");
                sb.Append("<th style='padding: 10px; border: 1px solid #ddd;'>Key</th>");
                sb.Append("<th style='padding: 10px; border: 1px solid #ddd;'>Value</th>");
                sb.Append("<th style='padding: 10px; border: 1px solid #ddd;'>Type</th>");
                sb.Append("</tr>");

                for (int i = 0; i < Session.Count; i++)
                {
                    string key = Session.Keys[i];
                    object value = Session[key];
                    string valueStr = value?.ToString() ?? "<null>";
                    string type = value?.GetType().Name ?? "null";

                    string rowColor = i % 2 == 0 ? "#f9f9f9" : "white";
                    sb.AppendFormat("<tr style='background-color: {0};'>", rowColor);
                    sb.AppendFormat("<td style='padding: 8px; border: 1px solid #ddd;'>{0}</td>", 
                        Server.HtmlEncode(key));
                    sb.AppendFormat("<td style='padding: 8px; border: 1px solid #ddd;'>{0}</td>", 
                        Server.HtmlEncode(valueStr));
                    sb.AppendFormat("<td style='padding: 8px; border: 1px solid #ddd;'>{0}</td>", 
                        Server.HtmlEncode(type));
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            litSessionContents.Text = sb.ToString();
        }

        protected void btnAddUpdate_Click(object sender, EventArgs e)
        {
            string key = txtKey.Text.Trim();
            string value = txtValue.Text.Trim();

            if (!string.IsNullOrEmpty(key))
            {
                Session[key] = value;
                
                // Clear the input fields
                txtKey.Text = "";
                txtValue.Text = "";
                
                UpdateDisplays();
            }
        }

        protected void btnRemove_Click(object sender, EventArgs e)
        {
            string key = txtKeyToRemove.Text.Trim();

            if (!string.IsNullOrEmpty(key) && Session[key] != null)
            {
                Session.Remove(key);
                
                // Clear the input field
                txtKeyToRemove.Text = "";
                
                UpdateDisplays();
            }
        }

        protected void btnClear_Click(object sender, EventArgs e)
        {
            Session.Clear();
            UpdateDisplays();
        }

        protected void btnRefresh_Click(object sender, EventArgs e)
        {
            // Simply refresh by redirecting to the same page
            Response.Redirect(Request.RawUrl);
        }

        protected void btnIncrementVisits_Click(object sender, EventArgs e)
        {
            int visitCount;
            if (!Int32.TryParse(Session["VisitCount"]?.ToString(), out visitCount))
            {
                visitCount = 0;
            }
            Session["VisitCount"] = visitCount + 1;
            UpdateDisplays();
        }

        protected void btnResetVisits_Click(object sender, EventArgs e)
        {
            Session["VisitCount"] = 0;
            UpdateDisplays();
        }
    }
}