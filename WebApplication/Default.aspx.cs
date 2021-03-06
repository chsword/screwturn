﻿
using System;
using System.Collections.Generic;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using ScrewTurn.Wiki.PluginFramework;
using System.Text;

namespace ScrewTurn.Wiki {

	public partial class DefaultPage : BasePage {

		private PageContent currentPage = null;
		private string currentWiki = null;

		private bool discussMode = false;
		private bool viewCodeMode = false;

		protected void Page_Load(object sender, EventArgs e) {
			currentWiki = DetectWiki();

			discussMode = Request["Discuss"] != null;
			viewCodeMode = Request["Code"] != null && !discussMode;
			if(!Settings.GetEnableViewPageCodeFeature(currentWiki)) viewCodeMode = false;

			currentPage = Pages.FindPage(currentWiki, DetectPage(true));

			VerifyAndPerformRedirects();

			// The following actions are verified:
			// - View content (redirect to AccessDenied)
			// - Edit or Edit with Approval (for button display)
			// - Any Administrative activity (Rollback/Admin/Perms) (for button display)
			// - Download attachments (for button display - download permissions are also checked in GetFile)
			// - View discussion (for button display in content mode)
			// - Post discussion (for button display in discuss mode)

			string currentUsername = SessionFacade.GetCurrentUsername();
			string[] currentGroups = SessionFacade.GetCurrentGroupNames(currentWiki);

			AuthChecker authChecker = new AuthChecker(Collectors.CollectorsBox.GetSettingsProvider(currentWiki));

			bool canView = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.ReadPage, currentUsername, currentGroups);
			bool canEdit = false;
			bool canEditWithApproval = false;
			Pages.CanEditPage(currentWiki, currentPage.FullName, currentUsername, currentGroups, out canEdit, out canEditWithApproval);
			if(canEditWithApproval && canEdit) canEditWithApproval = false;
			bool canDownloadAttachments = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.DownloadAttachments, currentUsername, currentGroups);
			bool canSetPerms = authChecker.CheckActionForGlobals(Actions.ForGlobals.ManagePermissions, currentUsername, currentGroups);
			bool canAdmin = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.ManagePage, currentUsername, currentGroups);
			bool canViewDiscussion = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.ReadDiscussion, currentUsername, currentGroups);
			bool canPostDiscussion = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.PostDiscussion, currentUsername, currentGroups);
			bool canManageDiscussion = authChecker.CheckActionForPage(currentPage.FullName, Actions.ForPages.ManageDiscussion, currentUsername, currentGroups);

			if(!canView) {
				if(SessionFacade.LoginKey == null) UrlTools.Redirect("Login.aspx?Redirect=" + Tools.UrlEncode(Tools.GetCurrentUrlFixed()));
				else UrlTools.Redirect(UrlTools.BuildUrl(currentWiki, "AccessDenied.aspx"));
			}
			attachmentViewer.Visible = canDownloadAttachments;

			attachmentViewer.PageFullName = currentPage.FullName;

			pnlPageInfo.Visible = Settings.GetEnablePageInfoDiv(currentWiki);

			SetupTitles();

			SetupToolbarLinks(canEdit || canEditWithApproval, canViewDiscussion, canPostDiscussion, canDownloadAttachments, canAdmin, canAdmin, canSetPerms);

			SetupLabels();
			SetupPrintAndRssLinks();
			SetupMetaInformation();
			VerifyAndPerformPageRedirection();
			SetupRedirectionSource();
			SetupNavigationPaths();
			SetupAdjacentPages();

			SessionFacade.Breadcrumbs(currentWiki).AddPage(currentPage.FullName);
			SetupBreadcrumbsTrail();

			SetupDoubleClickHandler();

			SetupEmailNotification();

			SetupPageContent(canPostDiscussion, canManageDiscussion);

			if(currentPage != null) {
				Literal canonical = new Literal();
				canonical.Text = Tools.GetCanonicalUrlTag(Request.Url.ToString(), currentPage.FullName, Pages.FindNamespace(currentWiki, NameTools.GetNamespace(currentPage.FullName)));
				Page.Header.Controls.Add(canonical);
			}
		}

		/// <summary>
		/// Verifies the need for a redirect and performs it.
		/// </summary>
		private void VerifyAndPerformRedirects() {
			if(currentPage == null) {
				UrlTools.Redirect(UrlTools.BuildUrl(currentWiki, "PageNotFound.aspx?Page=", Tools.UrlEncode(DetectFullName())));
			}
			if(Request["Edit"] == "1") {
				UrlTools.Redirect(UrlTools.BuildUrl(currentWiki, "Edit.aspx?Page=", Tools.UrlEncode(currentPage.FullName)));
			}
			if(Request["History"] == "1") {
				UrlTools.Redirect(UrlTools.BuildUrl(currentWiki, "History.aspx?Page=", Tools.UrlEncode(currentPage.FullName)));
			}
		}

		/// <summary>
		/// Sets the titles used in the page.
		/// </summary>
		private void SetupTitles() {
			string title = FormattingPipeline.PrepareTitle(currentWiki, currentPage.Title, false, FormattingContext.PageContent, currentPage.FullName);
			Page.Title = title + " - " + Settings.GetWikiTitle(currentWiki);
			lblPageTitle.Text = title;
		}

		/// <summary>
		/// Sets the content and visibility of all toolbar links.
		/// </summary>
		/// <param name="canEdit">A value indicating whether the current user can edit the page.</param>
		/// <param name="canViewDiscussion">A value indicating whether the current user can view the page discussion.</param>
		/// <param name="canPostMessages">A value indicating whether the current user can post messages in the page discussion.</param>
		/// <param name="canDownloadAttachments">A value indicating whether the current user can download attachments.</param>
		/// <param name="canRollback">A value indicating whether the current user can rollback the page.</param>
		/// <param name="canAdmin">A value indicating whether the current user can perform at least one administration task.</param>
		/// <param name="canSetPerms">A value indicating whether the current user can set page permissions.</param>
		private void SetupToolbarLinks(bool canEdit, bool canViewDiscussion, bool canPostMessages,
			bool canDownloadAttachments, bool canRollback, bool canAdmin, bool canSetPerms) {
			
			lblDiscussLink.Visible = !discussMode && !viewCodeMode && canViewDiscussion;
			if(lblDiscussLink.Visible) {
				lblDiscussLink.Text = string.Format(@"<a id=""DiscussLink"" title=""{0}"" href=""{3}?Discuss=1"">{1} ({2})</a>",
					Properties.Messages.Discuss, Properties.Messages.Discuss, Pages.GetMessageCount(currentPage),
					UrlTools.BuildUrl(currentWiki, NameTools.GetLocalName(currentPage.FullName), GlobalSettings.PageExtension));
			}

			lblEditLink.Visible = Settings.GetEnablePageToolbar(currentWiki) && !discussMode && !viewCodeMode && canEdit;
			if(lblEditLink.Visible) {
				lblEditLink.Text = string.Format(@"<a id=""EditLink"" title=""{0}"" href=""{1}"">{2}</a>",
					Properties.Messages.EditThisPage,
					UrlTools.BuildUrl(currentWiki, "Edit.aspx?Page=", Tools.UrlEncode(currentPage.FullName)),
					Properties.Messages.Edit);
			}

			if(Settings.GetEnablePageToolbar(currentWiki) && Settings.GetEnableViewPageCodeFeature(currentWiki)) {
				lblViewCodeLink.Visible = !discussMode && !viewCodeMode && !canEdit;
				if(lblViewCodeLink.Visible) {
					lblViewCodeLink.Text = string.Format(@"<a id=""ViewCodeLink"" title=""{0}"" href=""{2}?Code=1"">{1}</a>",
						Properties.Messages.ViewPageCode, Properties.Messages.ViewPageCode,
						UrlTools.BuildUrl(currentWiki, NameTools.GetLocalName(currentPage.FullName), GlobalSettings.PageExtension));
				}
			}
			else lblViewCodeLink.Visible = false;

			lblHistoryLink.Visible = Settings.GetEnablePageToolbar(currentWiki) && !discussMode && !viewCodeMode;
			if(lblHistoryLink.Visible) {
				lblHistoryLink.Text = string.Format(@"<a id=""HistoryLink"" title=""{0}"" href=""{1}"">{2}</a>",
					Properties.Messages.ViewPageHistory,
					UrlTools.BuildUrl(currentWiki, "History.aspx?Page=", Tools.UrlEncode(currentPage.FullName)),
					Properties.Messages.History);
			}

			int attachmentCount = GetAttachmentCount();
			lblAttachmentsLink.Visible = canDownloadAttachments && !discussMode && !viewCodeMode && attachmentCount > 0;
			if(lblAttachmentsLink.Visible) {
				lblAttachmentsLink.Text = string.Format(@"<a id=""PageAttachmentsLink"" title=""{0}"" href=""#"" onclick=""javascript:return __ToggleAttachmentsMenu(event.clientX, event.clientY);"">{1}</a>",
					Properties.Messages.Attachments, Properties.Messages.Attachments);
			}
			attachmentViewer.Visible = lblAttachmentsLink.Visible;

			int bakCount = GetBackupCount();
			lblAdminToolsLink.Visible = Settings.GetEnablePageToolbar(currentWiki) && !discussMode && !viewCodeMode &&
				((canRollback && bakCount > 0)|| canAdmin || canSetPerms);
			if(lblAdminToolsLink.Visible) {
				lblAdminToolsLink.Text = string.Format(@"<a id=""AdminToolsLink"" title=""{0}"" href=""#"" onclick=""javascript:return __ToggleAdminToolsMenu(event.clientX, event.clientY);"">{1}</a>",
					Properties.Messages.AdminTools, Properties.Messages.Admin);

				if(canRollback && bakCount > 0) {
					lblRollbackPage.Text = string.Format(@"<a href=""AdminPages.aspx?Rollback={0}"" onclick=""javascript:return __RequestConfirm();"" title=""{1}"">{2}</a>",
						Tools.UrlEncode(currentPage.FullName),
						Properties.Messages.RollbackThisPage, Properties.Messages.Rollback);
				}
				else lblRollbackPage.Visible = false;

				if(canAdmin) {
					lblAdministratePage.Text = string.Format(@"<a href=""AdminPages.aspx?Admin={0}"" title=""{1}"">{2}</a>",
						Tools.UrlEncode(currentPage.FullName),
						Properties.Messages.AdministrateThisPage, Properties.Messages.Administrate);
				}
				else lblAdministratePage.Visible = false;

				if(canSetPerms) {
					lblSetPagePermissions.Text = string.Format(@"<a href=""AdminPages.aspx?Perms={0}"" title=""{1}"">{2}</a>",
						Tools.UrlEncode(currentPage.FullName),
						Properties.Messages.SetPermissionsForThisPage, Properties.Messages.Permissions);
				}
				else lblSetPagePermissions.Visible = false;
			}

			lblPostMessageLink.Visible = discussMode && !viewCodeMode && canPostMessages;
			if(lblPostMessageLink.Visible) {
				lblPostMessageLink.Text = string.Format(@"<a id=""PostReplyLink"" title=""{0}"" href=""{1}"">{2}</a>",
					Properties.Messages.PostMessage,
					UrlTools.BuildUrl(currentWiki, "Post.aspx?Page=", Tools.UrlEncode(currentPage.FullName)),
					Properties.Messages.PostMessage);
			}

			lblBackLink.Visible = discussMode || viewCodeMode;
			if(lblBackLink.Visible) {
				lblBackLink.Text = string.Format(@"<a id=""BackLink"" title=""{0}"" href=""{1}"">{2}</a>",
					Properties.Messages.Back,
					UrlTools.BuildUrl(currentWiki, Tools.UrlEncode(currentPage.FullName), GlobalSettings.PageExtension, "?NoRedirect=1"),
					Properties.Messages.Back);
			}
		}

		/// <summary>
		/// Gets the number of backups for the current page.
		/// </summary>
		/// <returns>The number of backups.</returns>
		private int GetBackupCount() {
			return Pages.GetBackups(currentPage).Count;
		}

		/// <summary>
		/// Gets the number of attachments for the current page.
		/// </summary>
		/// <returns>The number of attachments.</returns>
		private int GetAttachmentCount() {
			int count = 0;
			foreach(IFilesStorageProviderV40 prov in Collectors.CollectorsBox.FilesProviderCollector.GetAllProviders(currentWiki)) {
				count += prov.ListPageAttachments(currentPage.FullName).Length;
			}
			return count;
		}

		/// <summary>
		/// Sets the content and visibility of all labels used in the page.
		/// </summary>
		private void SetupLabels() {
			if(discussMode) {
				lblModified.Visible = false;
				lblModifiedDateTime.Visible = false;
				lblBy.Visible = false;
				lblAuthor.Visible = false;
				lblCategorizedAs.Visible = false;
				lblPageCategories.Visible = false;
				lblNavigationPaths.Visible = false;
				lblDiscussedPage.Text = "<b>" + FormattingPipeline.PrepareTitle(currentWiki, currentPage.Title, false, FormattingContext.PageContent, currentPage.FullName) + "</b>";
			}
			else {
				lblPageDiscussionFor.Visible = false;
				lblDiscussedPage.Visible = false;

				lblModifiedDateTime.Text =
					Preferences.AlignWithTimezone(currentWiki, currentPage.LastModified).ToString(Settings.GetDateTimeFormat(currentWiki));
				lblAuthor.Text = Users.UserLink(currentWiki, currentPage.User);
				lblPageCategories.Text = GetFormattedPageCategories();
			}
		}

		/// <summary>
		/// Sets the Print and RSS links.
		/// </summary>
		private void SetupPrintAndRssLinks() {
			if(!viewCodeMode) {
				lblPrintLink.Text = string.Format(@"<a id=""PrintLink"" href=""{0}"" title=""{1}"" target=""_blank"">{2}</a>",
					UrlTools.BuildUrl(currentWiki, "Print.aspx?Page=", Tools.UrlEncode(currentPage.FullName), discussMode ? "&amp;Discuss=1" : ""),
					Properties.Messages.PrinterFriendlyVersion, Properties.Messages.Print);

				if(Settings.GetRssFeedsMode(currentWiki) != RssFeedsMode.Disabled) {
					lblRssLink.Text = string.Format(@"<a id=""RssLink"" href=""{0}"" title=""{1}"" target=""_blank""{2}>RSS</a>",
						UrlTools.BuildUrl(currentWiki, "RSS.aspx?Page=", Tools.UrlEncode(currentPage.FullName), discussMode ? "&amp;Discuss=1" : ""),
						discussMode ? Properties.Messages.RssForThisDiscussion : Properties.Messages.RssForThisPage,
						discussMode ? " class=\"discuss\"" : "");
				}
				else lblRssLink.Visible = false;
			}
			else {
				lblPrintLink.Visible = false;
				lblRssLink.Visible = false;
			}
		}

		/// <summary>
		/// Gets the categories for the current page, already formatted for display.
		/// </summary>
		/// <returns>The categories, formatted for display.</returns>
		private string GetFormattedPageCategories() {
			CategoryInfo[] categories = Pages.GetCategoriesForPage(currentPage);
			if(categories.Length == 0) {
				return string.Format(@"<i><a href=""{0}"" title=""{1}"">{2}</a></i>",
					GetCategoryLink("-"),
					Properties.Messages.Uncategorized, Properties.Messages.Uncategorized);
			}
			else {
				StringBuilder sb = new StringBuilder(categories.Length * 10);
				for(int i = 0; i < categories.Length; i++) {
					sb.AppendFormat(@"<a href=""{0}"" title=""{1}"">{2}</a>",
						GetCategoryLink(categories[i].FullName),
						NameTools.GetLocalName(categories[i].FullName),
						NameTools.GetLocalName(categories[i].FullName));
					if(i != categories.Length - 1) sb.Append(", ");
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Gets the link to a category.
		/// </summary>
		/// <param name="category">The full name of the category.</param>
		/// <returns>The link URL.</returns>
		private string GetCategoryLink(string category) {
			return UrlTools.BuildUrl(currentWiki, "AllPages.aspx?Cat=", Tools.UrlEncode(category));
		}

		/// <summary>
		/// Sets the content of the META description and keywords for the current page.
		/// </summary>
		private void SetupMetaInformation() {
			// Set keywords and description
			if(currentPage.Keywords != null && currentPage.Keywords.Length > 0) {
				Literal lit = new Literal();
				lit.Text = string.Format("<meta name=\"keywords\" content=\"{0}\" />", PrintKeywords(currentPage.Keywords));
				Page.Header.Controls.Add(lit);
			}
			if(!string.IsNullOrEmpty(currentPage.Description)) {
				Literal lit = new Literal();
				lit.Text = string.Format("<meta name=\"description\" content=\"{0}\" />", currentPage.Description);
				Page.Header.Controls.Add(lit);
			}
		}

		/// <summary>
		/// Prints the keywords in a CSV list.
		/// </summary>
		/// <param name="keywords">The keywords.</param>
		/// <returns>The list.</returns>
		private string PrintKeywords(string[] keywords) {
			StringBuilder sb = new StringBuilder(50);
			for(int i = 0; i < keywords.Length; i++) {
				sb.Append(keywords[i]);
				if(i != keywords.Length - 1) sb.Append(", ");
			}
			return sb.ToString();
		}

		/// <summary>
		/// Verifies the need for a page redirection, and performs it when appropriate.
		/// </summary>
		private void VerifyAndPerformPageRedirection() {
			if(currentPage == null) return;

			// Force formatting so that the destination can be detected
			FormattedContent.GetFormattedPageContent(currentWiki, currentPage);

			PageContent dest = Redirections.GetDestination(currentPage.FullName);
			if(dest == null) return;

			if(dest != null) {
				if(Request["NoRedirect"] != "1") {
					UrlTools.Redirect(dest.FullName + GlobalSettings.PageExtension + "?From=" + currentPage.FullName, false);
				}
				else {
					// Write redirection hint
					StringBuilder sb = new StringBuilder();
					sb.Append(@"<div id=""RedirectionDiv"">");
					sb.Append(Properties.Messages.ThisPageRedirectsTo);
					sb.Append(": ");
					sb.Append(@"<a href=""");
					UrlTools.BuildUrl(currentWiki, sb, "++", Tools.UrlEncode(dest.FullName), GlobalSettings.PageExtension, "?From=", Tools.UrlEncode(currentPage.FullName));
					sb.Append(@""">");
					sb.Append(FormattingPipeline.PrepareTitle(currentWiki, dest.Title, false, FormattingContext.PageContent, currentPage.FullName));
					sb.Append("</a></div>");
					Literal literal = new Literal();
					literal.Text = sb.ToString();
					plhContent.Controls.Add(literal);
				}
			}
		}

		/// <summary>
		/// Sets the breadcrumbs trail, if appropriate.
		/// </summary>
		private void SetupBreadcrumbsTrail() {
			if(Settings.GetDisableBreadcrumbsTrail(currentWiki) || discussMode || viewCodeMode) {
				lblBreadcrumbsTrail.Visible = false;
				return;
			}

			StringBuilder sb = new StringBuilder(1000);

			sb.Append(@"<div id=""BreadcrumbsDiv"">");

			string[] pageTrailTemp = SessionFacade.Breadcrumbs(currentWiki).GetAllPages();
			List<PageContent> pageTrail = new List<PageContent>(pageTrailTemp.Length);
			// Build a list of pages the are currently available
			foreach(string pageFullName in pageTrailTemp) {
				PageContent p = Pages.FindPage(currentWiki, pageFullName);
				if(p != null) pageTrail.Add(p);
			}
			int min = 3;
			if(pageTrail.Count < 3) min = pageTrail.Count;

			sb.Append(@"<div id=""BreadcrumbsDivMin"">");
			if(pageTrail.Count > 3) {
				// Write hyperLink
				sb.Append(@"<a href=""#"" onclick=""javascript:return __ShowAllTrail();"" title=""");
				sb.Append(Properties.Messages.ViewBreadcrumbsTrail);
				sb.Append(@""">(");
				sb.Append(pageTrail.Count.ToString());
				sb.Append(")</a> ");
			}

			for(int i = pageTrail.Count - min; i < pageTrail.Count; i++) {
				AppendBreadcrumb(sb, pageTrail[i], "s");
			}
			sb.Append("</div>");

			sb.Append(@"<div id=""BreadcrumbsDivAll"" style=""display: none;"">");
			// Write hyperLink
			sb.Append(@"<a href=""#"" onclick=""javascript:return __HideTrail();"" title=""");
			sb.Append(Properties.Messages.HideBreadcrumbsTrail);
			sb.Append(@""">[X]</a> ");
			for(int i = 0; i < pageTrail.Count; i++) {
				AppendBreadcrumb(sb, pageTrail[i], "f");
			}
			sb.Append("</div>");

			sb.Append("</div>");

			lblBreadcrumbsTrail.Text = sb.ToString();
		}

		/// <summary>
		/// Appends a breadbrumb trail element.
		/// </summary>
		/// <param name="sb">The destination <see cref="T:StringBuilder" />.</param>
		/// <param name="pageFullName">The full name of the page to append.</param>
		/// <param name="dpPrefix">The drop-down menu ID prefix.</param>
		private void AppendBreadcrumb(StringBuilder sb, PageContent page, string dpPrefix) {
			PageNameComparer comp = new PageNameComparer();
			
			// If the page does not exists return.
			if(page == null) return;
			
			string id = AppendBreadcrumbDropDown(sb, page.FullName, dpPrefix);

			string nspace = NameTools.GetNamespace(page.FullName);

			sb.Append("&raquo; ");
			if(comp.Compare(page, currentPage) == 0) sb.Append("<b>");
			sb.AppendFormat(@"<a href=""{0}"" title=""{1}""{2}{3}{4}>{1}</a>",
				Tools.UrlEncode(page.FullName) + GlobalSettings.PageExtension,
				FormattingPipeline.PrepareTitle(currentWiki, page.Title, false, FormattingContext.PageContent, currentPage.FullName) + (string.IsNullOrEmpty(nspace) ? "" : (" (" + NameTools.GetNamespace(page.FullName) + ")")),
				(id != null ? @" onmouseover=""javascript:return __ShowDropDown(event, '" + id + @"', this);""" : ""),
				(id != null ? @" id=""lnk" + id + @"""" : ""),
				(id != null ? @" onmouseout=""javascript:return __HideDropDown('" + id + @"');""" : ""));
			if(comp.Compare(page, currentPage) == 0) sb.Append("</b>");
			sb.Append(" ");
		}

		/// <summary>
		/// Appends the drop-down menu DIV with outgoing links for a page.
		/// </summary>
		/// <param name="sb">The destination <see cref="T:StringBuilder" />.</param>
		/// <param name="pageFullName">The page full name.</param>
		/// <param name="dbPrefix">The drop-down menu DIV ID prefix.</param>
		/// <returns>The DIV ID, or <c>null</c> if no target pages were found.</returns>
		private string AppendBreadcrumbDropDown(StringBuilder sb, string pageFullName, string dbPrefix) {
			// Build outgoing links list
			// Generate list DIV
			// Return DIV's ID

			string[] outgoingLinks = Pages.GetPageOutgoingLinks(currentWiki, pageFullName);
			if(outgoingLinks == null || outgoingLinks.Length == 0) return null;

			string id = dbPrefix + Guid.NewGuid().ToString();

			StringBuilder buffer = new StringBuilder(300);

			buffer.AppendFormat(@"<div id=""{0}"" style=""display: none;"" class=""pageoutgoinglinksmenu"" onmouseover=""javascript:return __CancelHideTimer();"" onmouseout=""javascript:return __HideDropDown('{0}');"">", id);
			int count = 0;
			foreach(string link in outgoingLinks) {
				PageContent target = Pages.FindPage(currentWiki, link);
				if(target != null) {
					count++;
					string title = FormattingPipeline.PrepareTitle(currentWiki, target.Title, false, FormattingContext.PageContent, currentPage.FullName);

					buffer.AppendFormat(@"<a href=""{0}{1}"" title=""{2}"">{2}</a>", link, GlobalSettings.PageExtension, title, title);
				}
				if(count >= 20) break;
			}
			buffer.Append("</div>");

			sb.Insert(0, buffer.ToString());

			if(count > 0) return id;
			else return null;
		}

		/// <summary>
		/// Sets the redirection source page link, if appropriate.
		/// </summary>
		private void SetupRedirectionSource() {
			if(Request["From"] != null) {

				PageContent source = Pages.FindPage(currentWiki, Request["From"]);

				if(source != null) {
					StringBuilder sb = new StringBuilder(300);
					sb.Append(@"<div id=""RedirectionInfoDiv"">");
					sb.Append(Properties.Messages.RedirectedFrom);
					sb.Append(": ");
					sb.Append(@"<a href=""");
					sb.Append(UrlTools.BuildUrl(currentWiki, "++", Tools.UrlEncode(source.FullName), GlobalSettings.PageExtension, "?NoRedirect=1"));
					sb.Append(@""">");
					sb.Append(FormattingPipeline.PrepareTitle(currentWiki, source.Title, false, FormattingContext.PageContent, currentPage.FullName));
					sb.Append("</a></div>");

					lblRedirectionSource.Text = sb.ToString();
				}
				else lblRedirectionSource.Visible = false;
			}
			else lblRedirectionSource.Visible = false;
		}

		/// <summary>
		/// Sets the navigation paths label.
		/// </summary>
		private void SetupNavigationPaths() {
			string[] paths = NavigationPaths.PathsPerPage(currentWiki, currentPage.FullName);

			string currentPath = Request["NavPath"];
			if(!string.IsNullOrEmpty(currentPath)) currentPath = currentPath.ToLowerInvariant();

			if(!discussMode && !viewCodeMode && paths.Length > 0) {
				StringBuilder sb = new StringBuilder(500);
				sb.Append(Properties.Messages.Paths);
				sb.Append(": ");
				for(int i = 0; i < paths.Length; i++) {
					NavigationPath path = NavigationPaths.Find(currentWiki, paths[i]);
					if(path != null) {
						if(currentPath != null && paths[i].ToLowerInvariant().Equals(currentPath)) sb.Append("<b>");

						sb.Append(@"<a href=""");
						sb.Append(UrlTools.BuildUrl(currentWiki, "Default.aspx?Page=", Tools.UrlEncode(currentPage.FullName), "&amp;NavPath=", Tools.UrlEncode(paths[i])));
						sb.Append(@""" title=""");
						sb.Append(NameTools.GetLocalName(path.FullName));
						sb.Append(@""">");
						sb.Append(NameTools.GetLocalName(path.FullName));
						sb.Append("</a>");

						if(currentPath != null && paths[i].ToLowerInvariant().Equals(currentPath)) sb.Append("</b>");
						if(i != paths.Length - 1) sb.Append(", ");
					}
				}

				lblNavigationPaths.Text = sb.ToString();
			}
			else lblNavigationPaths.Visible = false;
		}

		/// <summary>
		/// Prepares the previous and next pages link for navigation paths.
		/// </summary>
		/// <param name="previousPageLink">The previous page link.</param>
		/// <param name="nextPageLink">The next page link.</param>
		private void SetupAdjacentPages() {
			StringBuilder prev = new StringBuilder(50), next = new StringBuilder(50);

			if(Request["NavPath"] != null) {
				NavigationPath path = NavigationPaths.Find(currentWiki, Request["NavPath"]);

				if(path != null) {
					int idx = Array.IndexOf(path.Pages, currentPage.FullName);
					if(idx != -1) {
						if(idx > 0) {
							PageContent prevPage = Pages.FindPage(currentWiki, path.Pages[idx - 1]);
							prev.Append(@"<a href=""");
							UrlTools.BuildUrl(currentWiki, prev, "Default.aspx?Page=", Tools.UrlEncode(prevPage.FullName),
								"&amp;NavPath=", Tools.UrlEncode(path.FullName));

							prev.Append(@""" title=""");
							prev.Append(Properties.Messages.PrevPage);
							prev.Append(": ");
							prev.Append(FormattingPipeline.PrepareTitle(currentWiki, prevPage.Title, false, FormattingContext.PageContent, currentPage.FullName));
							prev.Append(@"""><b>&laquo;</b></a> ");
						}
						if(idx < path.Pages.Length - 1) {
							PageContent nextPage = Pages.FindPage(currentWiki, path.Pages[idx + 1]);
							next.Append(@" <a href=""");
							UrlTools.BuildUrl(currentWiki, next, "Default.aspx?Page=", Tools.UrlEncode(nextPage.FullName),
								"&amp;NavPath=", Tools.UrlEncode(path.FullName));

							next.Append(@""" title=""");
							next.Append(Properties.Messages.NextPage);
							next.Append(": ");
							next.Append(FormattingPipeline.PrepareTitle(currentWiki, nextPage.Title, false, FormattingContext.PageContent, currentPage.FullName));
							next.Append(@"""><b>&raquo;</b></a>");
						}
					}
				}
			}

			if(prev.Length > 0) {
				lblPreviousPage.Text = prev.ToString();
			}
			else lblPreviousPage.Visible = false;

			if(next.Length > 0) {
				lblNextPage.Text = next.ToString();
			}
			else lblNextPage.Visible = false;
		}

		/// <summary>
		/// Sets the JavaScript double-click editing handler.
		/// </summary>
		private void SetupDoubleClickHandler() {
			if(Settings.GetEnableDoubleClickEditing(currentWiki) && !discussMode && !viewCodeMode) {
				StringBuilder sb = new StringBuilder(200);
				sb.Append(@"<script type=""text/javascript"">" + "\n");
				sb.Append("<!--\n");
				sb.Append("document.ondblclick = function() {\n");
				sb.Append("document.location = '");
				sb.Append(UrlTools.BuildUrl(currentWiki, "Edit.aspx?Page=", Tools.UrlEncode(currentPage.FullName)));
				sb.Append("';\n");
				sb.Append("}\n");
				sb.Append("// -->\n");
				sb.Append("</script>");

				lblDoubleClickHandler.Text = sb.ToString();
			}
			else lblDoubleClickHandler.Visible = false;
		}

		/// <summary>
		/// Sets the email notification button.
		/// </summary>
		private void SetupEmailNotification() {
			if(SessionFacade.LoginKey != null && SessionFacade.CurrentUsername != "admin") {
				bool pageChanges = false;
				bool discussionMessages = false;

				UserInfo user = SessionFacade.GetCurrentUser(currentWiki);
				if(user != null && user.Provider.UsersDataReadOnly) {
					btnEmailNotification.Visible = false;
					return;
				}

				if(user != null) {
					Users.GetEmailNotification(user, currentPage.FullName, out pageChanges, out discussionMessages);
				}

				bool active = false;
				if(discussMode) {
					active = discussionMessages;
				}
				else {
					active = pageChanges;
				}

				if(active) {
					btnEmailNotification.CssClass = "activenotification" + (discussMode ? " discuss" : "");
					btnEmailNotification.ToolTip = Properties.Messages.EmailNotificationsAreActive;
				}
				else {
					btnEmailNotification.CssClass = "inactivenotification" + (discussMode ? " discuss" : "");
					btnEmailNotification.ToolTip = Properties.Messages.ClickToEnableEmailNotifications;
				}
			}
			else btnEmailNotification.Visible = false;
		}

		protected void btnEmailNotification_Click(object sender, EventArgs e) {
			bool pageChanges = false;
			bool discussionMessages = false;

			UserInfo user = SessionFacade.GetCurrentUser(currentWiki);
			if(user != null) {
				Users.GetEmailNotification(user, currentPage.FullName, out pageChanges, out discussionMessages);
			}

			if(discussMode) {
				Users.SetEmailNotification(currentWiki, user, currentPage.FullName, pageChanges, !discussionMessages);
			}
			else {
				Users.SetEmailNotification(currentWiki, user, currentPage.FullName, !pageChanges, discussionMessages);
			}

			SetupEmailNotification();
		}

		/// <summary>
		/// Sets the actual page content, based on the current view mode (normal, discussion, view code).
		/// </summary>
		/// <param name="canPostMessages">A value indicating whether the current user can post messages.</param>
		/// <param name="canManageDiscussion">A value indicating whether the current user can manage the discussion.</param>
		private void SetupPageContent(bool canPostMessages, bool canManageDiscussion) {
			if(!discussMode && !viewCodeMode) {
				Literal literal = new Literal();
				literal.Text = FormattedContent.GetFormattedPageContent(currentWiki, currentPage);
				plhContent.Controls.Add(literal);
			}
			else if(!discussMode && viewCodeMode) {
				if(Settings.GetEnableViewPageCodeFeature(currentWiki)) {
					Literal literal = new Literal();
					StringBuilder sb = new StringBuilder(currentPage.Content.Length + 100);
					sb.Append(@"<textarea style=""width: 98%; height: 500px;"" readonly=""true"">");
					sb.Append(Server.HtmlEncode(currentPage.Content));
					sb.Append("</textarea>");
					sb.Append("<br /><br />");
					sb.Append(Properties.Messages.MetaKeywords);
					sb.Append(": <b>");
					sb.Append(PrintKeywords(currentPage.Keywords));
					sb.Append("</b><br />");
					sb.Append(Properties.Messages.MetaDescription);
					sb.Append(": <b>");
					sb.Append(currentPage.Description);
					sb.Append("</b><br />");
					sb.Append(Properties.Messages.ChangeComment);
					sb.Append(": <b>");
					sb.Append(currentPage.Comment);
					sb.Append("</b>");
					literal.Text = sb.ToString();
					plhContent.Controls.Add(literal);
				}
			}
			else if(discussMode && !viewCodeMode) {
				PageDiscussion discussion = LoadControl("~/PageDiscussion.ascx") as PageDiscussion;
				discussion.CurrentPage = currentPage;
				discussion.CanPostMessages = canPostMessages;
				discussion.CanManageDiscussion = canManageDiscussion;
				plhContent.Controls.Add(discussion);
			}
		}

	}

}
