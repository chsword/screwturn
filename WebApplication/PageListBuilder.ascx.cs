﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using ScrewTurn.Wiki.PluginFramework;

namespace ScrewTurn.Wiki {

	public partial class PageListBuilder : System.Web.UI.UserControl {

		private string currentWiki = null;

		protected void Page_Load(object sender, EventArgs e) {
			currentWiki = Tools.DetectCurrentWiki();
			if(!Page.IsPostBack) {
				CurrentProvider = GlobalSettings.DefaultPagesProvider;
			}
		}

		/// <summary>
		/// Gets or sets the current namespace.
		/// </summary>
		public string CurrentNamespace {
			get { return ViewState["CN"] as string; }
			set { ViewState["CN"] = value; }
		}

		/// <summary>
		/// Gets or sets the current provider.
		/// </summary>
		public string CurrentProvider {
			get { return ViewState["CP"] as string; }
			set {
				ViewState["CP"] = value;
				ResetControl();
			}
		}

		/// <summary>
		/// Gets or the selected pages.
		/// </summary>
		public IList<string> SelectedPages {
			get {
				List<string> result = new List<string>(lstPages.Items.Count);
				
				foreach(ListItem item in lstPages.Items) {
					result.Add(item.Value);
				}

				return result;
			}
		}

		/// <summary>
		/// Resets the editor.
		/// </summary>
		public void ResetControl() {
			txtPageName.Text = "";
			lstAvailablePage.Items.Clear();
			btnAddPage.Enabled = false;
			lstPages.Items.Clear();
			btnRemove.Enabled = false;
		}

		protected void btnSearch_Click(object sender, EventArgs e) {
			//lstAvailablePage.Items.Clear();
			//btnAddPage.Enabled = false;

			//txtPageName.Text = txtPageName.Text.Trim();

			//if(txtPageName.Text.Length == 0) return;

			//PageContent[] pages = SearchTools.SearchSimilarPages(txtPageName.Text, CurrentNamespace, currentWiki);

			//string cp = CurrentProvider;

			//foreach(PageContent page in
			//    from p in pages
			//    where p.Provider.GetType().FullName == cp
			//    select p) {

			//    // Filter pages already in the list
			//    bool found = false;
			//    foreach(ListItem item in lstPages.Items) {
			//        if(item.Value == page.FullName) {
			//            found = true;
			//            break;
			//        }
			//    }

			//    if(!found) {
			//        lstAvailablePage.Items.Add(new ListItem(FormattingPipeline.PrepareTitle(currentWiki, page.Title, false, FormattingContext.Other, page.FullName), page.FullName));
			//    }
			//}

			//btnAddPage.Enabled = lstAvailablePage.Items.Count > 0;
		}

		protected void btnAddPage_Click(object sender, EventArgs e) {
			PageContent page = Pages.FindPage(currentWiki, lstAvailablePage.SelectedValue);

			lstPages.Items.Add(new ListItem(FormattingPipeline.PrepareTitle(currentWiki, page.Title, false, FormattingContext.Other, page.FullName), page.FullName));

			lstAvailablePage.Items.RemoveAt(lstAvailablePage.SelectedIndex);
			btnAddPage.Enabled = lstAvailablePage.Items.Count > 0;
		}

		protected void lstPages_SelectedIndexChanged(object sender, EventArgs e) {
			btnRemove.Enabled = lstPages.SelectedIndex != -1;
		}

		protected void btnRemove_Click(object sender, EventArgs e) {
			lstPages.Items.RemoveAt(lstPages.SelectedIndex);
			lstPages.SelectedIndex = -1;
			btnRemove.Enabled = false;
		}

	}

}
