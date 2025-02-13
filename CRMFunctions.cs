﻿// <copyright file="CRMFunctions.cs" company="Almad Solutions.">
// Copyright (c) 2017 All Rights Reserved
// </copyright>
// <author>Chris Adams</author>
// <date>12/7/2018</date>
// <summary>Handles calls to CRM.</summary>
namespace OptionSetEditor
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows.Forms;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Metadata;
    using Microsoft.Xrm.Sdk.Query;
    using XrmToolBox.Extensibility;

    /// <summary>
    /// Partial class to handle CRM functions.
    /// </summary>
    public partial class XrmOptionSetEditorControl
    {
        /// <summary>
        /// Retrieve the entities from CRM.
        /// </summary>
        /// <param name="solutionEntities">The entity collection,</param>
        private void GetEntities(EntityCollection solutionEntities)
        {
            this.WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving entities...",
                Work = (w, e) =>
                {
                    RetrieveAllEntitiesRequest request = new RetrieveAllEntitiesRequest();
                    var response = (RetrieveAllEntitiesResponse)Service.Execute(request);
                    var result = response.EntityMetadata.Join(solutionEntities.Entities.Select(x => x.Attributes["objectid"]), x => x.MetadataId, y => y, (x, y) => x);
                    EntityMetadata[] results = (EntityMetadata[])result.ToArray();
                    e.Result = results;
                },
                PostWorkCallBack = e =>
                {
                   EntitiesList.DataSource = new BindingList<EntityItem>(((EntityMetadata[])e.Result).Where(i =>
                   (!i.IsManaged.Value || (i.IsManaged.Value && i.IsCustomizable.Value)) && 
                   i.DisplayName.UserLocalizedLabel != null).Select(i => new EntityItem(i.DisplayName.UserLocalizedLabel.Label, i.LogicalName)).ToList());

                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        /// <summary>
        /// Retrieves the options from CRM for the selected attribute.
        /// </summary>
        /// <param name="attribute">The attribute whose options are to be retrieved.</param>
        private void GetOptions(EntityItem attribute)
        {
            var request = new RetrieveAttributeRequest
            {
                EntityLogicalName = attribute.Parent.LogicalName,
                LogicalName = attribute.LogicalName,
                RetrieveAsIfPublished = true
            };
            var response = (RetrieveAttributeResponse)Service.Execute(request);

            var attributeMetadata = (EnumAttributeMetadata)response.AttributeMetadata;

            OptionSetMetadata optionset = attributeMetadata.OptionSet;

            attribute.GlobalName = optionset.IsGlobal.GetValueOrDefault() ? optionset.Name : null;

            if (attribute.Global)
            {
                foreach (var item in EntitiesList.Items.Cast<EntityItem>())
                {
                    var existing = item.Children.Where(i => i.GlobalName == attribute.GlobalName && i.LogicalName != attribute.LogicalName && i.Parent.LogicalName != attribute.Parent.LogicalName);
                    if (existing.Count() > 0)
                    {
                        attribute.Children.AddRange(existing.First().Children);
                        attribute.Loaded = true;
                        OptionsList.DataSource = new BindingList<EntityItem>(attribute.Children);
                        AttributesList.DataSource = null;
                        AttributesList.DataSource = new BindingList<EntityItem>(attribute.Parent.Children.ToArray());
                        return;
                    }
                }
            }

            attribute.Children = new List<EntityItem>();
            attribute.Children.AddRange(optionset.Options.Select(r => new EntityItem(r.Value.GetValueOrDefault(), r.Label, r.Description, attribute)));

            this.Loading = true;
            OptionsList.DataSource = new BindingList<EntityItem>(attribute.Children);
            this.Loading = false;

            attribute.Loaded = true;

            if (attribute.Global)
            {
                var selected = AttributesList.SelectedItem;
                AttributesList.DataSource = null;
                AttributesList.DataSource = new BindingList<EntityItem>(attribute.Parent.Children.ToArray());
                AttributesList.SelectedItem = selected;
            }
        }

        /// <summary>
        /// Gets the attributes for the selected entity from CRM.
        /// </summary>
        /// <param name="entity">The entity whose attributes are to be retrieved.</param>
        private void GetAttributes(EntityItem entity)
        {
            this.WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving option attributes...",
                Work = (w, e) =>
                {
                    var request = new RetrieveEntityRequest
                    {
                        LogicalName = entity.LogicalName,
                        EntityFilters = EntityFilters.Attributes
                    };
                    var response = (RetrieveEntityResponse)Service.Execute(request);

                    e.Result = response.EntityMetadata;
                },
                PostWorkCallBack = e =>
                {
                    AttributeMetadata[] results = ((EntityMetadata)e.Result).Attributes;
                    entity.Children = new List<EntityItem>();
                    entity.Children.AddRange(results.Where(r => r.AttributeType == AttributeTypeCode.Picklist && r.DisplayName.UserLocalizedLabel != null).Select(r => new EntityItem(r.DisplayName.UserLocalizedLabel.Label, r.LogicalName, entity)));
                    AttributesList.DataSource = new BindingList<EntityItem>(entity.Children.ToArray());

                    entity.Loaded = true;
                    ImportMenu.Enabled = true;
                    ExportMenu.Enabled = true;
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        /// <summary>
        /// Gets the solutions form CRM.
        /// </summary>
        private void GetSolutions()
        {
            QueryExpression solutionQuery = new QueryExpression("solution");
            solutionQuery.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
            solutionQuery.ColumnSet = new ColumnSet("friendlyname", "uniquename", "publisherid");
            var solutions = Service.RetrieveMultiple(solutionQuery);
            List<ToolStripMenuItem> menus = new List<ToolStripMenuItem>();

            foreach (var solution in solutions.Entities)
            {
                if (solution["uniquename"].ToString().ToLower() == "default")
                {
                    this.DefaultPublisher = ((EntityReference)solution["publisherid"]).Id;
                }
                else
                {
                    ToolStripMenuItem solutionMenu = new ToolStripMenuItem();
                    solutionMenu.Size = new System.Drawing.Size(152, 22);
                    solutionMenu.Text = solution["friendlyname"].ToString();
                    solutionMenu.Click += this.SolutionMenu_Click;
                    solutionMenu.Tag = ((EntityReference)solution["publisherid"]).Id.ToString();
                    solutionMenu.Name = solution["uniquename"].ToString();
                    menus.Add(solutionMenu);
                }
            }

            SolutionEntitiesMenu.DropDownItems.AddRange(menus.ToArray());
        }

        /// <summary>
        /// Gets the entities for a given solution.
        /// </summary>
        /// <param name="solutionName">The solution name.</param>
        /// <param name="publisherId">The publisher id.</param>
        private void GetSolutionEntities(string solutionName, Guid? publisherId = null)
        {
            this.WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving solution items...",
                Work = (w, e) =>
                {
                    if (publisherId == null)
                    {
                        publisherId = DefaultPublisher;
                    }

                    var publisher = Service.Retrieve("publisher", publisherId.Value, new ColumnSet("customizationoptionvalueprefix"));
                    e.Result = publisher;
                },
                PostWorkCallBack = e =>
                {
                    Entity publisher = (Entity)e.Result;
                    OptionSetPrefix = (int)publisher["customizationoptionvalueprefix"];

                    QueryExpression componentsQuery = new QueryExpression
                    {
                        EntityName = "solutioncomponent",
                        ColumnSet = new ColumnSet("objectid"),
                        Criteria = new FilterExpression(),
                    };
                    LinkEntity solutionLink = new LinkEntity("solutioncomponent", "solution", "solutionid", "solutionid", JoinOperator.Inner);
                    solutionLink.LinkCriteria = new FilterExpression();
                    solutionLink.LinkCriteria.AddCondition(new ConditionExpression("uniquename", ConditionOperator.Equal, solutionName));
                    componentsQuery.LinkEntities.Add(solutionLink);
                    componentsQuery.Criteria.AddCondition(new ConditionExpression("componenttype", ConditionOperator.Equal, 1));
                    EntityCollection entities = Service.RetrieveMultiple(componentsQuery);
                    this.ExecuteMethod<EntityCollection>(GetEntities, entities);
                },
                AsyncArgument = null,
                IsCancelable = true,
                MessageWidth = 340,
                MessageHeight = 150
            });
        }

        /// <summary>
        /// Gets the current user from CRM.
        /// </summary>
        private void GetUser()
        {
            WhoAmIResponse whoResponse = (WhoAmIResponse)Service.Execute(new WhoAmIRequest());
            QueryByAttribute query = new QueryByAttribute("usersettings");
            query.Attributes.Add("systemuserid");
            query.Values.Add(whoResponse.UserId);
            query.ColumnSet = new ColumnSet("uilanguageid");

            var users = Service.RetrieveMultiple(query);

            this.DefaultLanguage = (int)users.Entities[0]["uilanguageid"];
        }
    }
}
