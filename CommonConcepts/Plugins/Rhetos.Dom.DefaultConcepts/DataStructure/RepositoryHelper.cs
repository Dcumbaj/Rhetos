﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhetos.Compiler;
using Rhetos.Dsl.DefaultConcepts;
using Rhetos.Dsl;
using Rhetos.Processing;
using Rhetos.Processing.DefaultCommands;

namespace Rhetos.Dom.DefaultConcepts
{
    public static class RepositoryHelper
    {
        public static readonly CsTag<DataStructureInfo> RepositoryAttributes = "RepositoryAttributes";
        public static readonly CsTag<DataStructureInfo> RepositoryInterfaces = new CsTag<DataStructureInfo>("RepositoryInterface", TagType.Appendable, ",\r\n        {0}");
        public static readonly CsTag<DataStructureInfo> RepositoryMembers = "RepositoryMembers";
        public static readonly CsTag<DataStructureInfo> QueryLoadedAssignPropertyTag = "QueryLoadedAssignProperty";
        public static readonly CsTag<DataStructureInfo> AssignSimplePropertyTag = "AssignSimpleProperty";

        private static string RepositorySnippet(DataStructureInfo info)
        {
            return string.Format(
@"{1}
    public class {0}_Repository : IRepository{2}
    {{
        private readonly Common.DomRepository _domRepository;
        private readonly Common.ExecutionContext _executionContext;

        public {0}_Repository(Common.DomRepository domRepository, Common.ExecutionContext executionContext)
        {{
            _domRepository = domRepository;
            _executionContext = executionContext;
        }}

{3}
    }}

", info.Name, RepositoryAttributes.Evaluate(info), RepositoryInterfaces.Evaluate(info), RepositoryMembers.Evaluate(info));
        }

        private static string CallFromModuleRepostiorySnippet(DataStructureInfo info)
        {
            return string.Format(
@"        private {0}_Repository _{0}_Repository;
        public {0}_Repository {0} {{ get {{ return _{0}_Repository ?? (_{0}_Repository = new {0}_Repository(_domRepository, _executionContext)); }} }}

", info.Name);
        }

        private static string RegisterRepository(DataStructureInfo info)
        {
            return string.Format(
            @"builder.RegisterType<{0}._Helper.{1}_Repository>().Keyed<IRepository>(""{0}.{1}"").InstancePerLifetimeScope();
            ",
                info.Module.Name,
                info.Name);
        }

        public static void GenerateRepository(DataStructureInfo info, ICodeBuilder codeBuilder)
        {
            codeBuilder.InsertCode(RepositorySnippet(info), ModuleCodeGenerator.HelperNamespaceMembersTag, info.Module);
            codeBuilder.InsertCode(CallFromModuleRepostiorySnippet(info), ModuleCodeGenerator.RepositoryMembersTag, info.Module);
            codeBuilder.InsertCode(RegisterRepository(info), ModuleCodeGenerator.CommonAutofacConfigurationMembersTag);
        }
        
        //==============================================================

        public static readonly CsTag<DataStructureInfo> BeforeQueryTag = "RepositoryBeforeQuery";

        private static string RepositoryReadFunctionsSnippet(DataStructureInfo info, string readFunctionBody)
        {
            return string.Format(
@"        public IEnumerable<{0}> Load(object parameter, Type parameterType)
        {{
            var items = _executionContext.GenericRepository(""{0}"").Load(parameter, parameterType);
            return (IEnumerable<{0}>)items;
        }}

        public IEnumerable<{0}> Read(object parameter, Type parameterType, bool preferQuery)
        {{
            var items = _executionContext.GenericRepository(""{0}"").Read(parameter, parameterType, preferQuery);
            return (IEnumerable<{0}>)items;
        }}

        [Obsolete(""Use Load() or Query() method."")]
        public global::{0}[] All()
        {{
            {1}
        }}

        [Obsolete(""Use Load() or Query() method."")]
        public global::{0}[] Filter(FilterAll filterAll)
        {{
            return All();
        }}

",
                info.GetKeyProperties(),
                readFunctionBody);
        }

        private static string RepositoryQueryFunctionsSnippet(DataStructureInfo info, string queryFunctionBody)
        {
            return string.Format(
@"        [Obsolete(""Use Load(identifiers) or Query(identifiers) method."")]
        public global::{0}.{1}[] Filter(IEnumerable<Guid> identifiers)
        {{
            const int BufferSize = 500; // EF 6.1.3 LINQ query has O(n^2) time complexity. Batch size of 500 results with optimal total time on the test system.
            int n = identifiers.Count();
            var result = new List<{0}.{1}>(n);
            for (int i = 0; i < (n+BufferSize-1) / BufferSize; i++)
            {{
                Guid[] idBuffer = identifiers.Skip(i*BufferSize).Take(BufferSize).ToArray();
                var itemBuffer = Query().Where(item => idBuffer.Contains(item.ID)).ToItems().ToArray();
                result.AddRange(itemBuffer);
            }}
            return result.ToArray();
        }}

        public IQueryable<Common.Queryable.{0}_{1}> Query()
        {{
            " + BeforeQueryTag.Evaluate(info) + @"
            " + queryFunctionBody + @"
        }}

        // LINQ to Entity does not support Query() method in subqueries.
        public IQueryable<Common.Queryable.{0}_{1}> Subquery {{ get {{ return Query(); }} }}

        public IQueryable<Common.Queryable.{0}_{1}> Query(object parameter, Type parameterType)
        {{
            var query = _executionContext.GenericRepository(""{0}.{1}"").Query(parameter, parameterType);
            return (IQueryable<Common.Queryable.{0}_{1}>)query;
        }}

",
                info.Module.Name,
                info.Name);
        }

        public static void GenerateReadableRepositoryFunctions(DataStructureInfo info, ICodeBuilder codeBuilder, string loadFunctionBody)
        {
            codeBuilder.InsertCode(RepositoryReadFunctionsSnippet(info, loadFunctionBody), RepositoryMembers, info);
            codeBuilder.InsertCode("IReadableRepository<" + info.Module.Name + "." + info.Name + ">", RepositoryInterfaces, info);
        }

        public static void GenerateQueryableRepositoryFunctions(DataStructureInfo info, ICodeBuilder codeBuilder, string queryFunctionBody, string loadFunctionBody = null)
        {
            if (loadFunctionBody == null)
                loadFunctionBody = "return Query().ToItems().ToArray();";
            GenerateReadableRepositoryFunctions(info, codeBuilder, loadFunctionBody);
            codeBuilder.InsertCode(RepositoryQueryFunctionsSnippet(info, queryFunctionBody), RepositoryMembers, info);
            codeBuilder.InsertCode("IQueryableRepository<Common.Queryable." + info.Module.Name + "_" + info.Name + ">", RepositoryInterfaces, info);

            codeBuilder.InsertCode(SnippetQueryListConversion(info), RepositoryMembers, info);
            codeBuilder.InsertCode(SnippetToItemsConversion(info), DomInitializationCodeGenerator.QueryExtensionsMembersTag);
            codeBuilder.InsertCode(SnippetToNavigationConversion(info), DataStructureCodeGenerator.BodyTag, info);
            codeBuilder.InsertCode(SnippetToItemConversion(info), DataStructureQueryableCodeGenerator.MembersTag, info);
            codeBuilder.InsertCode(SnippetLoadItemsConversion(info), DomInitializationCodeGenerator.QueryExtensionsMembersTag);
            codeBuilder.AddReferencesFromDependency(typeof(Rhetos.Utilities.Graph));
        }

        private static string SnippetQueryListConversion(DataStructureInfo info)
        {
            string queryableConstruction = (info is IOrmDataStructure)
                ? "_executionContext.EntityFrameworkContext.{0}_{1}.Create()"
                : "new Common.Queryable.{0}_{1}()";

            string filterByIds = (info is BrowseDataStructureInfo || info is IOrmDataStructure)
                ? "Filter(Query(), ids)"
                : "Query().Where(item => ids.Contains(item.ID))";

            string mapNavigationProperties = (info is IOrmDataStructure)
                ? @"if (item.ID == default(Guid))
                    q.ID = item.ID = Guid.NewGuid();
                _executionContext.EntityFrameworkContext.{0}_{1}.Attach(q);
                "
                : "";

            return string.Format(
@"        public IQueryable<Common.Queryable.{0}_{1}> QueryLoaded(IEnumerable<{0}.{1}> items)
        {{
            return items.Select(item =>
            {{
                var q = " + queryableConstruction + @";
                q.ID = item.ID;" + QueryLoadedAssignPropertyTag.Evaluate(info) + @"
                " + mapNavigationProperties + @"return q;
            }}).AsQueryable();
        }}

        public IQueryable<Common.Queryable.{0}_{1}> QueryPersisted(IEnumerable<{0}.{1}> items)
        {{
            var ids = items.Select(item => item.ID).ToList();
            return " + filterByIds + @";
        }}

        public List<Common.Queryable.{0}_{1}> LoadPersistedWithReferences(IEnumerable<{0}.{1}> items)
        {{
            var ids = items.Select(item => item.ID).ToList();
            var query = " + filterByIds + @";
            var loaded = query.ToList();
            Rhetos.Utilities.Graph.SortByGivenOrder(loaded, ids, item => item.ID);
            return loaded;
        }}

",
            info.Module.Name,
            info.Name);
        }

        private static string SnippetToItemsConversion(DataStructureInfo info)
        {
            return string.Format(@"public static IQueryable<{0}.{1}> ToItems(this IQueryable<Common.Queryable.{0}_{1}> query)
        {{
            return query.Select(item => new {0}.{1}
            {{
                ID = item.ID" + AssignSimplePropertyTag.Evaluate(info) + @"
            }});
        }}
        ",
            info.Module.Name,
            info.Name);
        }

        private static string SnippetToNavigationConversion(DataStructureInfo info)
        {
            return string.Format(@"/// <summary>Converts a simple object to a navigation object, and copies its simple properties. Navigation properties are set to null.</summary>
        public Common.Queryable.{0}_{1} ToNavigation()
        {{
            var item = this;
            return new Common.Queryable.{0}_{1}
            {{
                ID = item.ID" + RepositoryHelper.AssignSimplePropertyTag.Evaluate(info) + @"
            }};
        }}

        ",
            info.Module.Name,
            info.Name);
        }

        private static string SnippetToItemConversion(DataStructureInfo info)
        {
            return string.Format(@"public {0}.{1} ToItem()
        {{
            var item = this;
            return new {0}.{1}
            {{
                ID = item.ID" + RepositoryHelper.AssignSimplePropertyTag.Evaluate(info) + @"
            }};
        }}

        ",
            info.Module.Name,
            info.Name);
        }

        private static string SnippetLoadItemsConversion(DataStructureInfo info)
        {
            return string.Format(@"public static void LoadItems(ref IEnumerable<{0}.{1}> items)
        {{
            var query = items as IQueryable<Common.Queryable.{0}_{1}>;
            var navigationItems = items as IEnumerable<Common.Queryable.{0}_{1}>;

            if (query != null)
                items = query.ToItems().ToList(); // The IQueryable function allows ORM optimizations.
            else if (navigationItems != null)
                items = navigationItems.Select(item => item.ToItem()).ToList();
            else
            {{
                Rhetos.Utilities.CsUtility.Materialize(ref items);
                var itemsList = (IList<{0}.{1}>)items;
                for (int i = 0; i < itemsList.Count(); i++)
                {{
                    var navigationItem = itemsList[i] as Common.Queryable.{0}_{1};
                    if (navigationItem != null)
                        itemsList[i] = navigationItem.ToItem();
                }}
            }}
        }}
        ",
            info.Module.Name,
            info.Name);
        }
    }
}
