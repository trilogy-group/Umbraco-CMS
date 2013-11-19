﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Formatting;
using System.Web.Http;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Models.EntityBase;
using Umbraco.Core.Services;
using Umbraco.Web.Models.Trees;
using Umbraco.Web.Mvc;
using umbraco;
using umbraco.BusinessLogic.Actions;
using umbraco.businesslogic;
using umbraco.cms.businesslogic.web;
using umbraco.interfaces;
using Constants = Umbraco.Core.Constants;

namespace Umbraco.Web.Trees
{
    [LegacyBaseTree(typeof(loadContent))]
    [Tree(Constants.Applications.Content, Constants.Trees.Content, "Content")]
    [PluginController("UmbracoTrees")]
    [CoreTree]
    public class ContentTreeController : ContentTreeControllerBase
    {
        protected override TreeNode CreateRootNode(FormDataCollection queryStrings)
        {
            var node = base.CreateRootNode(queryStrings); 
            //if the user's start node is not default, then ensure the root doesn't have a menu
            if (Security.CurrentUser.StartContentId != Constants.System.Root)
            {
                node.MenuUrl = "";
            }
            return node;
        }

        protected override int RecycleBinId
        {
            get { return Constants.System.RecycleBinContent; }
        }

        protected override bool RecycleBinSmells
        {
            get { return Services.ContentService.RecycleBinSmells(); }
        }

        protected override int UserStartNode
        {
            get { return Security.CurrentUser.StartContentId; }
        }

        /// <summary>
        /// Gets the tree nodes for the given id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="queryStrings"></param>
        /// <returns></returns>
        /// <remarks>
        /// If the content item is a container node then we will not return anything
        /// </remarks>
        protected override TreeNodeCollection PerformGetTreeNodes(string id, FormDataCollection queryStrings)
        {
            var nodes = new TreeNodeCollection();

            var entities = GetChildEntities(id);

            foreach (var entity in entities)
            {
                var e = (UmbracoEntity)entity;
               
                var allowedUserOptions = GetAllowedUserMenuItemsForNode(e);
                if (CanUserAccessNode(e, allowedUserOptions))
                {                    

                    //Special check to see if it ia a container, if so then we'll hide children.
                    var isContainer = entity.AdditionalData.ContainsKey("IsContainer") 
                        && entity.AdditionalData["IsContainer"] is bool 
                        && (bool) entity.AdditionalData["IsContainer"];
                    
                    var node = CreateTreeNode(
                        e.Id.ToInvariantString(),
                        id,
                        queryStrings,
                        e.Name,
                        e.ContentTypeIcon,
                        e.HasChildren && (isContainer == false));

                    node.AdditionalData.Add("contentType", e.ContentTypeAlias);

                    if (isContainer)
                        node.SetContainerStyle();

                    if (e.IsPublished == false)
                        node.SetNotPublishedStyle();

                    if (e.HasPendingChanges)
                        node.SetHasUnpublishedVersionStyle();

                    if (Access.IsProtected(e.Id, e.Path))
                        node.SetProtectedStyle();
                    
                    nodes.Add(node);
                }
            }
            return nodes;
        }

        protected override MenuItemCollection PerformGetMenuForNode(string id, FormDataCollection queryStrings)
        {
            if (id == Constants.System.Root.ToInvariantString())
            {
                var menu = new MenuItemCollection();

                //if the user's start node is not the root then ensure the root menu is empty/doesn't exist
                if (Security.CurrentUser.StartContentId != Constants.System.Root)
                {
                    return menu;
                }

                //set the default to create
                menu.DefaultMenuAlias = ActionNew.Instance.Alias;

                // we need to get the default permissions as you can't set permissions on the very root node
                //TODO: Use the new services to get permissions
                var nodeActions = global::umbraco.BusinessLogic.Actions.Action.FromString(
                    UmbracoUser.GetPermissions(Constants.System.Root.ToInvariantString()))
                                        .Select(x => new MenuItem(x));

                //these two are the standard items
                menu.Items.Add<ActionNew>(ui.Text("actions", ActionNew.Instance.Alias));
                menu.Items.Add<ActionSort>(ui.Text("actions", ActionSort.Instance.Alias), true).ConvertLegacyMenuItem(null, "content", "content");

                //filter the standard items
                FilterUserAllowedMenuItems(menu, nodeActions);

                if (menu.Items.Any())
                {
                    menu.Items.Last().SeperatorBefore = true;
                }

                // add default actions for *all* users
                menu.Items.Add<ActionRePublish>(ui.Text("actions", ActionRePublish.Instance.Alias)).ConvertLegacyMenuItem(null, "content", "content");
                menu.Items.Add<RefreshNode, ActionRefresh>(ui.Text("actions", ActionRefresh.Instance.Alias), true);
                
                return menu;
            }


            //return a normal node menu:
            int iid;
            if (int.TryParse(id, out iid) == false)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
            var item = Services.EntityService.Get(iid, UmbracoObjectTypes.Document);
            if (item == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var nodeMenu = GetAllNodeMenuItems(item);
            var allowedMenuItems = GetAllowedUserMenuItemsForNode(item);
                
            FilterUserAllowedMenuItems(nodeMenu, allowedMenuItems);

            //if the media item is in the recycle bin, don't have a default menu, just show the regular menu
            if (item.Path.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Contains(RecycleBinId.ToInvariantString()))
            {
                nodeMenu.DefaultMenuAlias = null;
            }
            else
            {
                //set the default to create
                nodeMenu.DefaultMenuAlias = ActionNew.Instance.Alias;    
            }
            

            return nodeMenu;
        }

        protected override UmbracoObjectTypes UmbracoObjectType
        {
            get { return UmbracoObjectTypes.Document; }
        }

        /// <summary>
        /// Returns true or false if the current user has access to the node based on the user's allowed start node (path) access
        /// </summary>
        /// <param name="id"></param>
        /// <param name="queryStrings"></param>
        /// <returns></returns>
        protected override bool HasPathAccess(string id, FormDataCollection queryStrings)
        {
            var content = Services.ContentService.GetById(int.Parse(id));
            if (content == null)
            {
                return false;
            }
            return Security.CurrentUser.HasPathAccess(content);
        }

        /// <summary>
        /// Returns a collection of all menu items that can be on a content node
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        protected MenuItemCollection GetAllNodeMenuItems(IUmbracoEntity item)
        {
            var menu = new MenuItemCollection();
            menu.Items.Add<ActionNew>(ui.Text("actions", ActionNew.Instance.Alias));
            menu.Items.Add<ActionDelete>(ui.Text("actions", ActionDelete.Instance.Alias));
            
            //need to ensure some of these are converted to the legacy system - until we upgrade them all to be angularized.
            menu.Items.Add<ActionMove>(ui.Text("actions", ActionMove.Instance.Alias), true);
            menu.Items.Add<ActionCopy>(ui.Text("actions", ActionCopy.Instance.Alias));

            menu.Items.Add<ActionSort>(ui.Text("actions", ActionSort.Instance.Alias), true).ConvertLegacyMenuItem(item, "content", "content");

            menu.Items.Add<ActionRollback>(ui.Text("actions", ActionRollback.Instance.Alias)).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionPublish>(ui.Text("actions", ActionPublish.Instance.Alias), true).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionToPublish>(ui.Text("actions", ActionToPublish.Instance.Alias)).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionAssignDomain>(ui.Text("actions", ActionAssignDomain.Instance.Alias)).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionRights>(ui.Text("actions", ActionRights.Instance.Alias)).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionProtect>(ui.Text("actions", ActionProtect.Instance.Alias), true).ConvertLegacyMenuItem(item, "content", "content");
            
            menu.Items.Add<ActionNotify>(ui.Text("actions", ActionNotify.Instance.Alias), true).ConvertLegacyMenuItem(item, "content", "content");
            menu.Items.Add<ActionSendToTranslate>(ui.Text("actions", ActionSendToTranslate.Instance.Alias)).ConvertLegacyMenuItem(item, "content", "content");

            menu.Items.Add<RefreshNode, ActionRefresh>(ui.Text("actions", ActionRefresh.Instance.Alias), true);

            return menu;
        }

    }
}