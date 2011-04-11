
using System;
using System.IO;
using System.Resources;
using System.Security.Principal;
using System.Web.Configuration;
using ScrewTurn.Wiki.PluginFramework;
using System.Reflection;

namespace ScrewTurn.Wiki {

	/// <summary>
	/// Provides tools for starting and shutting down the wiki engine.
	/// </summary>
	public static class StartupTools {

		/// <summary>
		/// Gets the Settings Storage Provider configuration string from web.config.
		/// </summary>
		/// <returns>The configuration string.</returns>
		public static string GetSettingsStorageProviderConfiguration() {
			string config = WebConfigurationManager.AppSettings["SettingsStorageProviderConfig"];
			if(config != null) return config;
			else return "";
		}

		/// <summary>
		/// Updates the DLLs into the settings storage provider, if appropriate.
		/// </summary>
		/// <param name="provider">The provider.</param>
		/// <param name="settingsProviderAsmName">The file name of the assembly that contains the current Settings Storage Provider.</param>
		private static void UpdateDllsIntoSettingsProvider(ISettingsStorageProviderV30 provider, string settingsProviderAsmName) {
			// Look into public\Plugins (hardcoded)
			string fullPath = Path.Combine(Settings.PublicDirectory, "Plugins");

			if(!Directory.Exists(fullPath)) return;

			string[] dlls = Directory.GetFiles(fullPath, "*.dll");
			string[] installedDlls = provider.ListPluginAssemblies();

			foreach(string dll in dlls) {
				bool found = false;
				string filename = Path.GetFileName(dll);
				foreach(string instDll in installedDlls) {
					if(instDll.ToLowerInvariant() == filename.ToLowerInvariant()) {
						found = true;
						break;
					}
				}

				if(!found && filename.ToLowerInvariant() == settingsProviderAsmName.ToLowerInvariant()) {
					found = true;
				}

				if(found) {
					// Update DLL
					provider.StorePluginAssembly(filename, File.ReadAllBytes(dll));
				}
			}
		}

		/// <summary>
		/// Performs all needed startup operations.
		/// </summary>
		public static void Startup() {
			// Load Host
			Host.Instance = new Host();

			// Load config
			ISettingsStorageProviderV30 ssp = ProviderLoader.LoadSettingsStorageProvider(WebConfigurationManager.AppSettings["SettingsStorageProvider"]);
			ssp.Init(Host.Instance, GetSettingsStorageProviderConfiguration());
			ssp.SetUp();
			Collectors.SettingsProvider = ssp;

			Settings.CanOverridePublicDirectory = false;

			if(!(ssp is SettingsStorageProvider)) {
				// Update DLLs from public\Plugins
				UpdateDllsIntoSettingsProvider(ssp, ProviderLoader.SettingsStorageProviderAssemblyName);
			}

			if(ssp.IsFirstApplicationStart()) {
				if(ssp.GetMetaDataItem(MetaDataItem.AccountActivationMessage, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.AccountActivationMessage, null, Defaults.AccountActivationMessageContent);
				if(ssp.GetMetaDataItem(MetaDataItem.EditNotice, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.EditNotice, null, Defaults.EditNoticeContent);
				if(ssp.GetMetaDataItem(MetaDataItem.Footer, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.Footer, null, Defaults.FooterContent);
				if(ssp.GetMetaDataItem(MetaDataItem.Header, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.Header, null, Defaults.HeaderContent);
				if(ssp.GetMetaDataItem(MetaDataItem.PasswordResetProcedureMessage, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.PasswordResetProcedureMessage, null, Defaults.PasswordResetProcedureMessageContent);
				if(ssp.GetMetaDataItem(MetaDataItem.Sidebar, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.Sidebar, null, Defaults.SidebarContent);
				if(ssp.GetMetaDataItem(MetaDataItem.PageChangeMessage, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.PageChangeMessage, null, Defaults.PageChangeMessage);
				if(ssp.GetMetaDataItem(MetaDataItem.DiscussionChangeMessage, null) == "")
					ssp.SetMetaDataItem(MetaDataItem.DiscussionChangeMessage, null, Defaults.DiscussionChangeMessage);
				if(ssp.GetMetaDataItem(MetaDataItem.ApproveDraftMessage, null) == "") {
					ssp.SetMetaDataItem(MetaDataItem.ApproveDraftMessage, null, Defaults.ApproveDraftMessage);
				}
			}

			MimeTypes.Init();

			// Load Providers
			Collectors.FileNames = new System.Collections.Generic.Dictionary<string, string>(10);
			Collectors.InitCollectors();

			// Load built-in providers

			// Files storage providers have to be loaded BEFORE users storage providers in order to properly set permissions
				ProviderLoader.SetUp<IFilesStorageProviderV30>(typeof(FilesStorageProvider));
				Collectors.AddProvider(typeof(FilesStorageProvider), Assembly.GetAssembly(typeof(FilesStorageProvider)), typeof(IFilesStorageProviderV30), !ProviderLoader.IsDisabled(typeof(IFilesStorageProviderV30).FullName));
			
			ProviderLoader.SetUp<IThemeStorageProviderV30>(typeof(ThemeStorageProvider));
			Collectors.AddProvider(typeof(ThemeStorageProvider), Assembly.GetAssembly(typeof(ThemeStorageProvider)), typeof(IThemeStorageProviderV30), !ProviderLoader.IsDisabled(typeof(ThemeStorageProvider).FullName));
			
			ProviderLoader.SetUp<IUsersStorageProviderV30>(typeof(UsersStorageProvider));
			Collectors.AddProvider(typeof(UsersStorageProvider), Assembly.GetAssembly(typeof(UsersStorageProvider)), typeof(IUsersStorageProviderV30), !ProviderLoader.IsDisabled(typeof(UsersStorageProvider).FullName));
			
			// Load Users (pages storage providers might need access to users/groups data for upgrading from 2.0 to 3.0)
			ProviderLoader.FullLoad(true, false, false, false);

			bool groupsCreated = VerifyAndCreateDefaultGroups();
			
			ProviderLoader.SetUp<IPagesStorageProviderV30>(typeof(PagesStorageProvider));
			Collectors.AddProvider(typeof(PagesStorageProvider), Assembly.GetAssembly(typeof(PagesStorageProvider)), typeof(IPagesStorageProviderV30), !ProviderLoader.IsDisabled(typeof(PagesStorageProvider).FullName));
			
			// Load all other providers
			ProviderLoader.FullLoad(false, true, true, true);

			if(groupsCreated) {
				// It is necessary to set default permissions for file management
				UserGroup administratorsGroup = Users.FindUserGroup(Settings.AdministratorsGroup);
				UserGroup anonymousGroup = Users.FindUserGroup(Settings.AnonymousGroup);
				UserGroup usersGroup = Users.FindUserGroup(Settings.UsersGroup);

				SetAdministratorsGroupDefaultPermissions(administratorsGroup);
				SetUsersGroupDefaultPermissions(usersGroup);
				SetAnonymousGroupDefaultPermissions(anonymousGroup);
			}

			// Create the Main Page, if needed
			if(Pages.FindPage(Settings.DefaultPage) == null) CreateMainPage();

			Log.LogEntry("ScrewTurn Wiki is ready", EntryType.General, Log.SystemUsername);

			System.Threading.ThreadPool.QueueUserWorkItem(state => {
				using(((WindowsIdentity)state).Impersonate()) {
					if((DateTime.Now - Settings.LastPageIndexing).TotalDays > 7) {
						Settings.LastPageIndexing = DateTime.Now;
						System.Threading.Thread.Sleep(10000);
						using(MemoryStream ms = new MemoryStream()) {
							using(StreamWriter wr = new System.IO.StreamWriter(ms)) {
								System.Web.HttpContext.Current = new System.Web.HttpContext(new System.Web.Hosting.SimpleWorkerRequest("", "", wr));
								foreach(var provider in Collectors.CollectorsBox.PagesProviderCollector.AllProviders) {
									if(!provider.ReadOnly) {
										Log.LogEntry("Starting automatic rebuilding index for provider: " + provider.Information.Name, EntryType.General, Log.SystemUsername);
										provider.RebuildIndex();
										Log.LogEntry("Finished automatic rebuilding index for provider: " + provider.Information.Name, EntryType.General, Log.SystemUsername);
									}
								}
							}
						}
					}
				}
			}, WindowsIdentity.GetCurrent());
		}

		/// <summary>
		/// Verifies the existence of the default user groups and creates them if necessary.
		/// </summary>
		/// <returns><c>true</c> if the groups were created, <c>false</c> otherwise.</returns>
		private static bool VerifyAndCreateDefaultGroups() {
			UserGroup administratorsGroup = Users.FindUserGroup(Settings.AdministratorsGroup);
			UserGroup anonymousGroup = Users.FindUserGroup(Settings.AnonymousGroup);
			UserGroup usersGroup = Users.FindUserGroup(Settings.UsersGroup);

			// Create default groups if they don't exist already, initializing permissions

			bool aGroupWasCreated = false;

			if(administratorsGroup == null) {
				Users.AddUserGroup(Settings.AdministratorsGroup, "Built-in Administrators");
				administratorsGroup = Users.FindUserGroup(Settings.AdministratorsGroup);

				aGroupWasCreated = true;
			}

			if(usersGroup == null) {
				Users.AddUserGroup(Settings.UsersGroup, "Built-in Users");
				usersGroup = Users.FindUserGroup(Settings.UsersGroup);

				aGroupWasCreated = true;
			}

			if(anonymousGroup == null) {
				Users.AddUserGroup(Settings.AnonymousGroup, "Built-in Anonymous Users");
				anonymousGroup = Users.FindUserGroup(Settings.AnonymousGroup);

				aGroupWasCreated = true;
			}

			if(aGroupWasCreated) {
				ImportPageDiscussionPermissions();
			}

			return aGroupWasCreated;
		}

		/// <summary>
		/// Creates the main page.
		/// </summary>
		private static void CreateMainPage() {
			Pages.CreatePage(null as string, Settings.DefaultPage);
			Pages.ModifyPage(Pages.FindPage(Settings.DefaultPage), "Main Page", Log.SystemUsername,
				DateTime.Now, "", Defaults.MainPageContent, null, null, SaveMode.Normal);
		}

		/// <summary>
		/// Performs shutdown operations, such as shutting-down Providers.
		/// </summary>
		public static void Shutdown() {
			Collectors.CollectorsBox.Dispose();
			Settings.Provider.Dispose();
		}

		/// <summary>
		/// Sets the default permissions for the administrators group, properly importing version 2.0 values.
		/// </summary>
		/// <param name="administrators">The administrators group.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		public static bool SetAdministratorsGroupDefaultPermissions(UserGroup administrators) {
			// Administrators can do any operation
			return AuthWriter.SetPermissionForGlobals(AuthStatus.Grant, Actions.FullControl, administrators);

			// Settings.ConfigVisibleToAdmins is not imported on purpose
		}

		/// <summary>
		/// Sets the default permissions for the users group, properly importing version 2.0 values.
		/// </summary>
		/// <param name="users">The users group.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		public static bool SetUsersGroupDefaultPermissions(UserGroup users) {
			bool done = true;

			// Set namespace-related permissions
			if(Settings.UsersCanCreateNewPages) {
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.CreatePages, users);
			}
			else done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ModifyPages, users);
			done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.PostDiscussion, users);
			if(Settings.UsersCanCreateNewCategories || Settings.UsersCanManagePageCategories) {
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ManageCategories, users);
			}

			done &= SetupFileManagementPermissions(users);

			return done;
		}

		/// <summary>
		/// Sets the default permissions for the anonymous users group, properly importing version 2.0 values.
		/// </summary>
		/// <param name="anonymous">The anonymous users group.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		public static bool SetAnonymousGroupDefaultPermissions(UserGroup anonymous) {
			bool done = true;

			// Properly import Private/Public Mode wiki
			if(Settings.PrivateAccess) {
				// Nothing to do, because without any explicit grant, Anonymous users cannot do anything
			}
			else if(Settings.PublicAccess) {
				// Public access, allow modification and propagate file management permissions if they were allowed for anonymous users
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ModifyPages, anonymous);
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.DownloadAttachments, anonymous);
				if(Settings.UsersCanCreateNewPages) {
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.CreatePages, anonymous);
				}
				if(Settings.UsersCanCreateNewCategories || Settings.UsersCanManagePageCategories) {
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ManageCategories, anonymous);
				}
				if(Settings.FileManagementInPublicAccessAllowed) {
					SetupFileManagementPermissions(anonymous);
				}
			}
			else {
				// Standard configuration, only allow read permissions
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ReadPages, anonymous);
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.ReadDiscussion, anonymous);
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.DownloadAttachments, anonymous);

				foreach(IFilesStorageProviderV30 prov in Collectors.CollectorsBox.FilesProviderCollector.AllProviders) {
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.DownloadFiles, anonymous);
				}
			}

			return done;
		}

		/// <summary>
		/// Sets file management permissions for the users or anonymous users group, importing version 2.0 values.
		/// </summary>
		/// <param name="group">The group.</param>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		private static bool SetupFileManagementPermissions(UserGroup group) {
			bool done = true;

			if(Settings.UsersCanViewFiles) {
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.DownloadAttachments, group);
				foreach(IFilesStorageProviderV30 prov in Collectors.CollectorsBox.FilesProviderCollector.AllProviders) {
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.DownloadFiles, group);
				}
			}
			if(Settings.UsersCanUploadFiles) {
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.UploadAttachments, group);
				foreach(IFilesStorageProviderV30 prov in Collectors.CollectorsBox.FilesProviderCollector.AllProviders) {
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.UploadFiles, group);
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.CreateDirectories, group);
				}
			}
			if(Settings.UsersCanDeleteFiles) {
				done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.DeleteAttachments, group);
				foreach(IFilesStorageProviderV30 prov in Collectors.CollectorsBox.FilesProviderCollector.AllProviders) {
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.DeleteFiles, group);
					done &= AuthWriter.SetPermissionForDirectory(AuthStatus.Grant, prov, "/", Actions.ForDirectories.DeleteDirectories, group);
				}
			}

			return done;
		}

		/// <summary>
		/// Imports version 2.0 page discussion settings and properly propagates them to user groups and single pages, when needed.
		/// </summary>
		/// <returns><c>true</c> if the operation succeeded, <c>false</c> otherwise.</returns>
		private static bool ImportPageDiscussionPermissions() {
			// Notes
			// Who can read pages, can read discussions
			// Who can modify pages, can post messages and read discussions
			// Who can manage pages, can manage discussions and post messages

			// Possible values: page|normal|locked|public
			string value = Settings.DiscussionPermissions.ToLowerInvariant();

			UserGroup usersGroup = Users.FindUserGroup(Settings.UsersGroup);
			UserGroup anonymousGroup = Users.FindUserGroup(Settings.AnonymousGroup);

			bool done = true;

			switch(value) {
				case "page":
					// Nothing to do
					break;
				case "normal":
					// Allow Users to post messages
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.PostDiscussion, usersGroup);
					break;
				case "locked":
					// Deny Users to post messages
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Deny, null, Actions.ForNamespaces.PostDiscussion, usersGroup);
					break;
				case "public":
					// Allow Users and Anonymous Users to post messages
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.PostDiscussion, usersGroup);
					done &= AuthWriter.SetPermissionForNamespace(AuthStatus.Grant, null, Actions.ForNamespaces.PostDiscussion, anonymousGroup);
					break;
			}

			return true;
		}

	}

}
