﻿using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.DrunkenToad.Core.Models;
using PlayerTrack.Domain.Common;
using PlayerTrack.Infrastructure;
using PlayerTrack.Models;

namespace PlayerTrack.Domain;

using System.Threading.Tasks;
using Dalamud.DrunkenToad.Collections;
using Dalamud.DrunkenToad.Core;

public class CategoryService : CacheService<Category>
{
    private PlayerFilter playerCategoryFilter = new();
    private List<string> categoryNames = new();
    private List<string> categoryNamesWithBlank = new() { string.Empty };
    private List<string> categoryNamesExclDynamic = new() { string.Empty };
    private List<string> categoryNamesExclDynamicWithBlank  = new() { string.Empty };

    public CategoryService() => this.ReloadCategoryCache();

    public static int GetDefaultCategory(ToadLocation loc)
    {
        DalamudContext.PluginLog.Verbose($"Entering CategoryService.GetDefaultCategory(): {loc.LocationType}");
        var config = ServiceContext.ConfigService.GetConfig().GetTrackingLocationConfig(loc.LocationType);
        return config.DefaultCategoryId != 0 ? config.DefaultCategoryId : 0;
    }

    public Category? GetCategory(int id) => this.cache.FindFirst(cat => cat.Id == id);

    public Category? GetSyncedCategory(int socialListId) => this.cache.FindFirst(cat => cat.SocialListId == socialListId);
    
    public List<Category> GetCategories(bool includeDynamic = true)
    {
        return includeDynamic ? this.cache.GetAll().OrderBy(cat => cat.Rank).ToList() : 
            this.cache.GetAll().Where(cat => cat.SocialListId == 0).OrderBy(cat => cat.Rank).ToList();
    }

    public Dictionary<int, int> GetCategoryRanks() => this.GetCategories().ToDictionary(cat => cat.Id, cat => cat.Rank);

    public PlayerFilter GetCategoryFilters() => this.playerCategoryFilter;

    public void CreateCategory(string name, int socialListId = 0)
    {
        DalamudContext.PluginLog.Verbose($"Entering CategoryService.CreateCategory(): {name}");
        var rank = 1;

        var categories = this.GetCategories();
        if (categories.Count > 0)
        {
            var maxCategory = categories.Aggregate((max, cat) => cat.Id > max.Id || cat.Rank > max.Rank ? cat : max);
            rank += maxCategory.Rank;
        }

        var category = new Category
        {
            Rank = rank,
            Name = name,
            SocialListId = socialListId,
        };

        this.AddCategoryToCacheAndRepository(category);
    }

    public void UpdateCategory(Category category)
    {
        DalamudContext.PluginLog.Verbose($"Entering CategoryService.UpdateCategory(): {category.Name}");
        this.UpdateCategoryInCacheAndRepository(category);
    }

    public void DeleteCategory(Category category) => Task.Run(() =>
    {
        DalamudContext.PluginLog.Verbose($"Entering CategoryService.DeleteCategory(): {category.Name}");
        var categories = this.GetCategories();

        var filteredCategories = categories.Where(cat => cat.Rank > category.Rank).ToList();
        foreach (var cat in filteredCategories)
        {
            cat.Rank -= 1;
            this.UpdateCategory(cat);
        }

        this.DeleteCategoryFromCacheAndRepository(category);
    });

    public List<string> GetCategoryNames(bool includeBlank = true, bool includeDynamic = true)
    {
        if (includeDynamic)
        {
            return includeBlank ? this.categoryNamesWithBlank : this.categoryNames;
        }
        return includeBlank ? this.categoryNamesExclDynamicWithBlank : this.categoryNamesExclDynamic;
    }

    public void DecreaseCategoryRank(int id) => this.SwapCategoryRanks(id, 1);

    public void IncreaseCategoryRank(int id) => this.SwapCategoryRanks(id, -1);

    public bool IsMaxRankCategory(Category category)
    {
        try
        {
            var categories = this.GetCategories();
            return category.Rank == categories.Max(cat => cat.Rank);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool IsMinRankCategory(Category category)
    {
        try
        {
            var categories = this.GetCategories();
            return category.Rank == categories.Min(cat => cat.Rank);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void RefreshCategories()
    {
        DalamudContext.PluginLog.Verbose("CategoryService.RefreshCategories()");
        this.ReloadCategoryCache();
    }

    public Category? GetCategoryByName(string name) => this.cache.FindFirst(category => category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? null;

    public Category? GetCategoryById(int categoryId) => this.cache.Get(categoryId);

    private static void PopulateDerivedFields(Category category)
    {
        if (category.Id == 0)
        {
            category.PlayerConfig = new PlayerConfig(PlayerConfigType.Category);
            return;
        }

        category.PlayerConfig = RepositoryContext.PlayerConfigRepository.GetPlayerConfigByCategoryId(category.Id) ?? new PlayerConfig(PlayerConfigType.Category);
    }

    private void BuildCategoryNames()
    {
        DalamudContext.PluginLog.Verbose("Entering CategoryService.BuildCategoryNames()");
        var categories = this.GetCategories();
        this.categoryNames = categories.Select(cat => cat.Name).ToList();
        this.categoryNamesWithBlank = new List<string> { string.Empty }.Concat(this.categoryNames).ToList();
        this.categoryNamesExclDynamic = categories.Where(cat => cat.SocialListId == 0).Select(cat => cat.Name).ToList();
        this.categoryNamesExclDynamicWithBlank = new List<string> { string.Empty }.Concat(this.categoryNamesExclDynamic).ToList();
    }

    private void BuildCategoryFilters()
    {
        DalamudContext.PluginLog.Verbose("Entering CategoryService.BuildCategoryFilters()");
        var categoriesByRank = this.GetCategories();
        var totalCategories = categoriesByRank.Count;

        var categoryFilterIds = categoriesByRank.Select(category => category.Id).ToList();
        var categoryFilterNames = categoriesByRank.Select(category => category.Name).ToList();

        categoryFilterIds.Insert(0, 0);
        categoryFilterNames.Insert(0, string.Empty);

        this.playerCategoryFilter = new PlayerFilter
        {
            FilterIds = categoryFilterIds,
            FilterNames = categoryFilterNames,
            TotalFilters = totalCategories,
        };
    }

    private void SwapCategoryRanks(int id, int rankOffset) => Task.Run(() =>
    {
        var categories = this.GetCategories();

        var currentCategory = categories.FirstOrDefault(cat => cat.Id == id);
        if (currentCategory == null)
        {
            return;
        }

        var swapCategory = categories.FirstOrDefault(cat => cat.Rank == currentCategory.Rank + rankOffset);
        if (swapCategory == null)
        {
            return;
        }

        currentCategory.Rank += rankOffset;
        swapCategory.Rank -= rankOffset;

        this.UpdateCategory(currentCategory);
        this.UpdateCategory(swapCategory);

        var expectedRank = 1;
        foreach (var category in categories.OrderBy(cat => cat.Rank))
        {
            if (category.Rank != expectedRank)
            {
                category.Rank = expectedRank;
                this.UpdateCategory(category);
            }
            expectedRank++;
        }
        
        ServiceContext.PlayerDataService.RecalculatePlayerRankings();
    });

    private void ReloadCategoryCache() => this.ExecuteReloadCache(() =>
    {
        var categories = RepositoryContext.CategoryRepository.GetAllCategories();

        if (categories == null)
        {
            this.cache = new ThreadSafeCollection<int, Category>();
        }

        var collection = new ThreadSafeCollection<int, Category>(categories!.ToDictionary(cat => cat.Id));

        foreach (var category in collection.GetAll())
        {
            PopulateDerivedFields(category);
        }

        this.cache = collection;
        this.BuildCategoryFilters();
        this.BuildCategoryNames();
    });

    private void UpdateCategoryInCacheAndRepository(Category category)
    {
        DalamudContext.PluginLog.Verbose($"Entering CategoryService.UpdateCategoryInCacheAndRepository(): {category.Name}");
        this.cache.Update(category.Id, category);
        RepositoryContext.CategoryRepository.UpdateCategory(category);
        this.BuildCategoryFilters();
        this.BuildCategoryNames();
        var players = ServiceContext.PlayerCacheService.GetCategoryPlayers(category.Id);
        foreach (var player in players)
        {
            ServiceContext.PlayerCacheService.PopulateDerivedFields(player);
            ServiceContext.VisibilityService.SyncWithVisibility(player);
        }
        this.OnCacheUpdated();
    }

    private void AddCategoryToCacheAndRepository(Category category)
    {
        category.Id = RepositoryContext.CategoryRepository.CreateCategory(category);
        category.PlayerConfig = new PlayerConfig(PlayerConfigType.Category);
        PopulateDerivedFields(category);
        this.cache.Add(category.Id, category);
        this.BuildCategoryFilters();
        this.BuildCategoryNames();
        ServiceContext.PlayerCacheService.AddCategory(category.Id);
        ServiceContext.PlayerDataService.RecalculatePlayerRankings();
        // ServiceContext.PlayerCacheService.LoadPlayers(); // disable for now due to lock contention
        this.OnCacheUpdated();
    }

    private void DeleteCategoryFromCacheAndRepository(Category category)
    {
        PlayerCategoryService.DeletePlayerCategoryByCategoryId(category.Id);
        PlayerConfigService.DeletePlayerConfigByCategoryId(category.Id);
        RepositoryContext.CategoryRepository.DeleteCategory(category.Id);
        var config = ServiceContext.ConfigService.GetConfig();
        config.ClearCategoryIds(category.Id);
        ServiceContext.ConfigService.SaveConfig(config);
        this.cache.Remove(category.Id);
        ServiceContext.CategoryService.RefreshCategories();
        ServiceContext.PlayerDataService.ClearCategoryFromPlayers(category.Id);
        ServiceContext.PlayerDataService.RecalculatePlayerRankings();
        this.OnCacheUpdated();
    }
}
