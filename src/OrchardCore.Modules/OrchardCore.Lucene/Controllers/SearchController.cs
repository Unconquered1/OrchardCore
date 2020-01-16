using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.QueryParsers.Classic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using OrchardCore.DisplayManagement;
using OrchardCore.Lucene.Model;
using OrchardCore.Lucene.Services;
using OrchardCore.Navigation;
using OrchardCore.Search.Abstractions.ViewModels;
using OrchardCore.Settings;
using YesSql;
using YesSql.Services;

namespace OrchardCore.Lucene.Controllers
{
    public class SearchController : Controller
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly ISiteService _siteService;
        private readonly LuceneIndexManager _luceneIndexProvider;
        private readonly LuceneIndexingService _luceneIndexingService;
        private readonly LuceneIndexSettingsService _luceneIndexSettingsService;
        private readonly LuceneAnalyzerManager _luceneAnalyzerManager;
        private readonly ISearchQueryService _searchQueryService;
        private readonly ISession _session;
        private readonly dynamic New;

        public SearchController(
            IAuthorizationService authorizationService,
            ISiteService siteService,
            LuceneIndexManager luceneIndexProvider,
            LuceneIndexingService luceneIndexingService,
            LuceneIndexSettingsService luceneIndexSettingsService,
            LuceneAnalyzerManager luceneAnalyzerManager,
            ISearchQueryService searchQueryService,
            ISession session,
            IShapeFactory shapeFactory,
            ILogger<SearchController> logger
            )
        {
            _authorizationService = authorizationService;
            _siteService = siteService;
            _luceneIndexProvider = luceneIndexProvider;
            _luceneIndexingService = luceneIndexingService;
            _luceneIndexSettingsService = luceneIndexSettingsService;
            _luceneAnalyzerManager = luceneAnalyzerManager;
            _searchQueryService = searchQueryService;
            _session = session;
            New = shapeFactory;

            Logger = logger;
        }

        ILogger Logger { get; set; }

        [HttpGet]
        public async Task<IActionResult> Index(SearchIndexViewModel viewModel, PagerSlimParameters pagerParameters)
        {
            if (!await _authorizationService.AuthorizeAsync(User, Permissions.QueryLuceneSearch))
            {
                return Unauthorized();
            }

            var siteSettings = await _siteService.GetSiteSettingsAsync();

            if (!_luceneIndexProvider.Exists(viewModel.IndexName))
            {
                Logger.LogInformation("Couldn't execute search. The search index doesn't exist.");
                return BadRequest("Search is not configured.");
            }

            var luceneSettings = await _luceneIndexingService.GetLuceneSettingsAsync();

            if (luceneSettings == null || luceneSettings?.DefaultSearchFields == null)
            {
                Logger.LogInformation("Couldn't execute search. No Lucene settings was defined.");
                return BadRequest("Search is not configured.");
            }

            var luceneIndexSettings = await _luceneIndexSettingsService.GetSettingsAsync(viewModel.IndexName);

            if (luceneIndexSettings == null)
            {
                Logger.LogInformation($"Couldn't execute search. No Lucene index settings was defined for ({viewModel.IndexName}) index.");
                return BadRequest($"Search index ({viewModel.IndexName}) is not configured.");
            }

            if (string.IsNullOrWhiteSpace(viewModel.Terms))
            {
                return View(new SearchIndexViewModel
                {
                    IndexName = viewModel.IndexName,
                    ContentItems = Enumerable.Empty<ContentItem>()
                });
            }

            var pager = new PagerSlim(pagerParameters, siteSettings.PageSize);

            //We Query Lucene index
            var analyzer = _luceneAnalyzerManager.CreateAnalyzer(await _luceneIndexSettingsService.GetIndexAnalyzerAsync(luceneIndexSettings.IndexName));
            var queryParser = new MultiFieldQueryParser(LuceneSettings.DefaultVersion, luceneSettings.DefaultSearchFields, analyzer);
            var query = queryParser.Parse(QueryParser.Escape(viewModel.Terms));

            // Fetch one more result than PageSize to generate "More" links
            var start = 0;
            var end = pager.PageSize + 1;

            if (pagerParameters.Before != null)
            {
                start = Convert.ToInt32(pagerParameters.Before) - pager.PageSize - 1;
                end = Convert.ToInt32(pagerParameters.Before);
            }
            else if (pagerParameters.After != null)
            {
                start = Convert.ToInt32(pagerParameters.After);
                end = Convert.ToInt32(pagerParameters.After) + pager.PageSize + 1;
            }

            var contentItemIds = await _searchQueryService.ExecuteQueryAsync(query, viewModel.IndexName, start, end);

            //We Query database to retrieve content items.
            IQuery<ContentItem> queryDb;
            
            if (luceneIndexSettings.IndexLatest)
            {
                queryDb = _session.Query<ContentItem, ContentItemIndex>()
                    .Where(x => x.ContentItemId.IsIn(contentItemIds) && x.Latest == true)
                    .Take(pager.PageSize + 1);
            }
            else
            {
                queryDb = _session.Query<ContentItem, ContentItemIndex>()
                    .Where(x => x.ContentItemId.IsIn(contentItemIds) && x.Published == true)
                    .Take(pager.PageSize + 1);
            }

            var containedItems = await queryDb.ListAsync();

            //We set the PagerSlim before and after links
            if (pagerParameters.After != null || pagerParameters.Before != null)
            {
                if (start + 1 > 1)
                {
                    pager.Before = (start + 1).ToString();
                }
                else
                {
                    pager.Before = null;
                }
            }

            if (containedItems.Count() == pager.PageSize + 1)
            {
                pager.After = (end - 1).ToString();
            }
            else
            {
                pager.After = null;
            }

            var model = new SearchIndexViewModel
            {
                Terms = viewModel.Terms,
                Pager = (await New.PagerSlim(pager)).UrlParams(new Dictionary<string, object>() { { "IndexName", viewModel.IndexName }, { "Terms", viewModel.Terms } }),
                IndexName = viewModel.IndexName,
                ContentItems = containedItems.Take(pager.PageSize)
            };

            return View(model);
        }
    }
}