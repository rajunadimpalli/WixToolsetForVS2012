using System;
using System.IO;
using Microsoft.Deployment.WindowsInstaller;
using Microsoft.Web.Administration;
using System.Security.Principal;

namespace MyCustomActions
{
    public class CustomActions
    {
        /// <summary>
        /// Adds the II7 web sites and the application pool names to the 
        /// ComboBox table in the MSI file.
        /// </summary>
        /// <param name="session">
        /// The installer session.
        /// </param>
        /// <returns>
        /// 
        /// Always returns ActionResult.Success, otherwise rethrows the error
        /// encountered.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the <paramref name="session"/> parameter is null.
        /// </exception>
        [CustomAction]
        public static ActionResult EnumerateIISWebSitesAndAppPools(Session session)
        {


            if (null == session)
            {
                throw new ArgumentNullException("session");
            }

            session.Log("EnumerateIISWebSitesAndAppPools: Begin");

            ActionResult result;
            // Check if running with admin rights and if not, log a message to
            // let them know why it's failing.
            if (false == HasAdminRights())
            {
                session.Log("EnumerateIISWebSitesAndAppPools: " +
                            "ATTEMPTING TO RUN WITHOUT ADMIN RIGHTS");
                return ActionResult.Failure;
            }

            session.Log("EnumerateIISWebSitesAndAppPools: " +
                                    "Getting the IIS 7 management object");

            using (var iisManager = new ServerManager())
            {
                result = EnumSitesIntoComboBox(session, iisManager);
                if (ActionResult.Success == result)
                {
                    result = EnumAppPoolsIntoComboBox(session, iisManager);
                }
            }

            session.Log("EnumerateIISWebSitesAndAppPools: End");
            return result;
        }


        [CustomAction]
        public static ActionResult UpdatePropsWithSelectedWebSite(Session session)
        {
            try
            {
                string selectedWebSiteId = session["selectedItem"];

                session.Log("CA: Found web site id: " + selectedWebSiteId);
                View availableWebSitesView = session.Database.OpenView("Select * from AvailableWebSites where WebSiteNo=" + selectedWebSiteId);
                availableWebSitesView.Execute();

                Record record = availableWebSitesView.Fetch();
                if ((record[1].ToString()) == selectedWebSiteId)
                {
                    session["WEBSITE_DESCRIPTION"] = (string)record[2];
                }
                string appPoolSelected = session["AppPoolselectedItem"];
                View availableAppPoolView = session.Database.OpenView("select * from AvailableAppPools where AppPoolNo= " + appPoolSelected);
                availableAppPoolView.Execute();
                Record record1 = availableAppPoolView.Fetch();
                if ((record1[1].ToString()) == appPoolSelected)
                {
                    session["APP_POOLNAME"] = (string)record1[2];
                }

            }
            catch (Exception ex)
            {
                session.Log("CustomActionException: " + ex);

                return ActionResult.Failure;

            }

            return ActionResult.Success;

        }

        /// <summary>
        /// Sets the INSTALLDIR property to the directory where the web site 
        /// defaults to.
        /// </summary>
        /// <param name="session">
        /// The installer session.
        /// </param>
        /// <returns>
        /// Returns ActionResult.Success if the web site properly exists. 
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the <paramref name="session"/> parameter is null.
        /// </exception>
        [CustomAction]
        public static ActionResult SetInstallDirBasedOnSelectedWebSite(Session session)
        {
            if (null == session)
            {
                throw new ArgumentNullException("session");
            }
            try
            {
                // Debugger.Break();
                session.Log("SetInstallDir: Begin");

                // Let's get the selected website.
                String webSite = session["WEBSITE_NAME"];
                session.Log("SetInstallDir: Working with the web site: {0}", webSite);

                // Grab that web sites based physical directory and get it's "/"
                // (base path physical directory).
                String basePath;
                using (var iisManager = new ServerManager())
                {
                    Site site = iisManager.Sites[webSite];
                    basePath = site.Applications["/"].
                                           VirtualDirectories["/"].PhysicalPath;
                }

                session.Log("SetInstallDir: Physical path : {0}", basePath);

                // Environment variables are used in IIS7 so expand them.
                basePath = Environment.ExpandEnvironmentVariables(basePath);

                // Get the web application name and poke that onto the end.
                String webAppName = session["WEB_APP_NAME"];
                String finalPath = Path.Combine(basePath, webAppName);

                // Set INSTALLDIR to the calculate path.
                session.Log("SetInstallDir: Setting INSTALLLOCATION to {0}",
                            finalPath);
                //session["INSTALLDIR"] = finalPath;
                session["INSTALLLOCATION"] = finalPath;
            }
            catch (Exception ex)
            {
                session.Log("SetInstallDir: exception: {0}", ex.Message);
                throw;
            }

            return ActionResult.Success;
        }

        /// <summary>
        /// The custom action to get the application pool for the selected web
        /// site the user wants to install to in WebVDirInstallDlg.WXS. This 
        /// will set the APP_POOL_NAME property.
        /// </summary>
        /// <param name="session">
        /// The installer session.
        /// </param>
        /// <returns>
        /// Returns ActionResult.Success if the web site properly exists. 
        /// ActionResult.Failure otherwise.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown if the <paramref name="session"/> parameter is null.
        /// </exception>
        [CustomAction]
        public static ActionResult SetAppPoolNameToWebSiteDefault(Session session)
        {
            if (null == session)
            {
                throw new ArgumentNullException("session");
            }

            try
            {
                // Debugger.Break();
                session.Log("SetAppPoolName: Begin");

                // Let's get the selected website.
                String webSite = session["WEBSITE_NAME"];
                session.Log("SetAppPoolName: Working with the web site: {0}", webSite);

                session.Log("SetAppPoolName: Getting the IIS 7 ServerManager");
                using (var iisManager = new ServerManager())
                {

                    session.Log("SetAppPoolName: Getting the app pool.");
                    string appPool = iisManager.Sites[webSite].Applications["/"].
                                                            ApplicationPoolName;
                    session.Log("SetAppPoolName: Found the app pool: {0}",
                                 appPool);
                    session["APP_POOL_NAME"] = appPool;
                    session.Log("SetAppPoolName: Set the APP_POOL_NAME property.");
                }
            }
            catch (Exception ex)
            {
                session.Log("SetAppPoolName: exception: {0}",
                            ex.Message);
                throw;
            }

            return ActionResult.Success;
        }

        private static ActionResult EnumAppPoolsIntoComboBox(Session session, ServerManager iisManager)
        {
            session.Log("EnumAppPools: Begin");
            try
            {
                // Grab the combo box.
                View view = session.Database.OpenView("SELECT * FROM ComboBox WHERE ComboBox.Property='APP_POOL_NAME'");
                view.Execute();

                Int32 index = 1;
                session.Log("EnumAppPools: Enumerating the app pools");
                foreach (var pool in iisManager.ApplicationPools)
                {
                    // Create a record for this app pool. All I care about is
                    // the name so use it for fields three and four.
                    session.Log("EnumAppPools: Processing app pool: {0}",
                                 pool.Name);
                    Record record = session.Database.CreateRecord(4);
                    record.SetString(1, "APP_POOL_NAME");
                    record.SetInteger(2, index);
                    record.SetString(3, pool.Name);
                    record.SetString(4, pool.Name);

                    session.Log("EnumAppPools: Adding app pool record");
                    view.Modify(ViewModifyMode.InsertTemporary, record);
                    index++;
                }

                view.Close();

                session.Log("EnumAppPools: End");
            }
            catch (Exception ex)
            {
                session.Log("EnumAppPools exception: {0}", ex.Message);
                throw;
            }

            return ActionResult.Success;
        }

        private static ActionResult EnumSitesIntoComboBox(Session session, ServerManager iisManager)
        {
            try
            {
                // Debugger.Break();
                session.Log("EnumSites: Begin");

                // Grab the combo box but make sure I'm getting only the one 
                // from WebAppInstallDlg.
                View view = session.Database.OpenView("SELECT * FROM ComboBox WHERE ComboBox.Property='WEBSITE_NAME'");
                view.Execute();

                Int32 index = 1;
                session.Log("EnumSites: Enumerating the sites");
                foreach (Site site in iisManager.Sites)
                {
                    // Create a record for this web site. All I care about is
                    // the name so use it for fields three and four.
                    session.Log("EnumSites: Processing site: {0}", site.Name);
                    Record record = session.Database.CreateRecord(4);
                    record.SetString(1, "WEBSITE_NAME");
                    record.SetInteger(2, index);
                    record.SetString(3, site.Name);
                    record.SetString(4, site.Name);

                    session.Log("EnumSites: Adding record");
                    view.Modify(ViewModifyMode.InsertTemporary, record);
                    index++;
                }

                view.Close();

                session.Log("EnumSites: End");
            }
            catch (Exception ex)
            {
                session.Log("EnumSites: exception: {0}", ex.Message);
                throw;
            }

            return ActionResult.Success;
        }

        static bool HasAdminRights()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        [CustomAction]
        public static ActionResult GetShisDatabaseConnectionString(Session session)
        {
            string machineName = Environment.MachineName;
            session.Log("Made it past getting the machine name " + machineName);
            if (machineName == "CURRENT MACHINE NAME") // Machine name on where you are installing the msi
            {
                session["USERID"] = "test"; // give your database server connection string user id
                session["PASSWORD"] = "test"; // database password
                session["DATASOURCE"] = "DEV"; // data source name
            }
          
            else
            {
                session["USERID"] = "local";
                session["PASSWORD"] = "local";
                session["DATASOURCE"] = "LOCAL";
            }

            session.Log(string.Format("The connecitons string:  {0},{1},{2}", session["USERID"], session["PASSWORD"], session["DATASOURCE"]));

            return ActionResult.Success;


        }

        [CustomAction]
        public static ActionResult GetServiceLocationUrlByMachineName(Session session)
        {
            string adminValue;
            // get the machine name
            var machineName = System.Environment.MachineName;
            string appServerName;
            
            if (machineName == "MACHINENAME")//you can hardcode the machine name or get automatically
            {
                appServerName = "SERVERNAME";
                session["INSTANCE"] = "DEV";
                session["SMTPSERVERNAME"] = "YOUR SMTP SERVER NAME";
            }
           
            else
            {
                appServerName = "localhost";
                session["INSTANCE"] = "DEV";
            }

            session.Log("Machine Name:" + machineName);
            session.Log("App server name:" + appServerName);
            session["APPSERVERNAME"] = appServerName;

            adminValue = "http://" + appServerName + "/YourService/YourService.svc";

            session.Log("compelete webservice url:" + adminValue);
            session["YOURSERVICELOCATION"] = adminValue;


            return ActionResult.Success;
        }
    }
}
