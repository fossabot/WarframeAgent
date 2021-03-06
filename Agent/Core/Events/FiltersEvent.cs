﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace Core.Events
{
    // TODO: rename to FilterManager
    public class FiltersEvent
    {
        private CancellationTokenSource _cts;
        private Task _mainTask;

        /// <summary>
        ///     Данные успешно обновлены.
        /// </summary>
        public event Action Updated;

        /// <summary>
        ///     Фильтр "Items" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> ItemsUpdated;

        /// <summary>
        ///     Фильтр "Sectors" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> SectorsUpdated;

        /// <summary>
        ///     Фильтр "Missions" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> MissionsUpdated;

        /// <summary>
        ///     Фильтр "Factions" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> FactionsUpdated;

        /// <summary>
        ///     Фильтр "Builds" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> BuildsUpdated;

        /// <summary>
        ///     Фильтр "Planets" успешно обновлен.
        /// </summary>
        public event EventHandler<EventArgs> PlanetsUpdated;

        public async Task Start()
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                await RunInitialPopulation(ct);
            }
            catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
            {
                Tools.Logging.Send(LogLevel.Debug, $"Управление фильтрами: начальная загрузка отменена", ex);
                return;
            }
            _mainTask = RunFilterUpdateLoop(ct);
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            _cts = null;
            if (_mainTask != null)
            {
                try
                {
                    await _mainTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        #pragma warning disable CS0649
        struct NameUriPair { public string name; public string url; }
        #pragma warning restore CS0649
        async Task<Dictionary<Model.Filter.Type, Uri>> FetchFilterAddresses(CancellationToken ct)
        {
            var fetchUri = Settings.Program.Urls.Filter;
            Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: получаю адреса свежих фильтров из {fetchUri}");
            try
            {
                var fetchedJson = await Tools.Network.ReadTextAsync(fetchUri, TimeSpan.FromSeconds(10), ct);
                Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: получил адреса свежих фильтров");
                var uris = await Task.Run(() => // parse in the background
                {
                    var values = JsonConvert.DeserializeObject<Dictionary<string, NameUriPair[]>>(fetchedJson);
                    var result = new Dictionary<Model.Filter.Type, Uri>();
                    foreach (var p in values["rus"]) //TODO: Перевод на другие языки.
                    {
                        if (!Enum.TryParse<Model.Filter.Type>(p.name, ignoreCase: true, result: out var type))
                        {
                            Tools.Logging.Send(LogLevel.Error, $"Управление фильтрами: в новых адресах фильтров не распознан тип {p.name}");
                            continue;
                        }
                        if (!Uri.TryCreate(p.url, UriKind.Absolute, out var uri))
                        {
                            Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: в новых адресах фильтров не распознан адрес для типа {p.name}");
                            continue;
                        }
                        if (result.TryGetValue(type, out _))
                        {
                            Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: в новых адресах фильтров дубликат значения для типа {p.name}");
                            continue;
                        }
                        result.Add(type, uri);
                    }
                    return result;
                }, ct);
                Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: разбор адресов фильтров окончен");

                var currentUris = Settings.Program.Filters.Content;
                // если есть отличия, запишем на диск
                if (!(uris.OrderBy(t => t.Key).SequenceEqual(currentUris.OrderBy(t => t.Key))))
                {
                    Tools.Logging.Send(LogLevel.Info, "Управление фильтрами: получены новые адреса фильтров, сохраняю");
                    Settings.Program.Filters.Content = uris;
                    Settings.Program.Save();
                }
                else
                {
                    Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: изменений в адресах фильтров нет");
                }

                return uris;
            }
            catch (Exception ex)
            {
                Tools.Logging.Send(LogLevel.Warn, $"Управление фильтрами: Ошибка при загрузке и разборе списка адресов {fetchUri}.", ex);
                return Settings.Program.Filters.Content;
            }
        }

        static string GetFilterFilePath(Model.Filter.Type type) => $"{Settings.Program.Directories.Data}/Filters/{type}.json";
        static readonly IEnumerable<Model.Filter.Type> SupportedFilterTypes =
            new[]
            {
                Model.Filter.Type.Items,
                Model.Filter.Type.Planets,
                Model.Filter.Type.Missions,
                Model.Filter.Type.Factions,
                Model.Filter.Type.Builds,
                Model.Filter.Type.Planets_new
            };

        Dictionary<Model.Filter.Type, int> versions = SupportedFilterTypes.ToDictionary(k => k, k => -1);

        async Task RunInitialPopulation(CancellationToken ct)
        {
            Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: начальная загрузка сохранённых фильтров");
            foreach (var type in SupportedFilterTypes)
            {
                ct.ThrowIfCancellationRequested();
                string path = GetFilterFilePath(type);
                Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: начальная загрузка фильтра {type} из файла {path}");
                string filterText;
                try
                {
                    var expandedPath = Model.StorageModel.ExpandRelativeName(path);
                    filterText = await Tools.File.ReadAllTextAsync(expandedPath, TimeSpan.FromSeconds(3), ct);
                    Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: фильтр {type} прочитан из файла {path}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Tools.Logging.Send(LogLevel.Warn, $"Управление фильтрами: ошибка при чтении {path}.", e);
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                var currentVersion = versions[type];
                var newVersion = await TryUpdateFilter(currentVersion, type, filterText, false, ct);
                versions[type] = newVersion;
            }
            Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: начальная загрузка сохранённых фильтров окончена");
        }

        async Task RunFilterUpdateLoop(CancellationToken ct)
        {
            Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: запускаем цикл обновления");
            try
            {
                Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: получаем адреса фильтров");
                var uris = await FetchFilterAddresses(ct);
                foreach (var type in SupportedFilterTypes)
                {
                    if (!uris.ContainsKey(type))
                        Tools.Logging.Send(LogLevel.Warn, $"Управление фильтрами: нету адреса фильтра для {type}");
                }
                Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: адреса фильтров получены");
                while (!ct.IsCancellationRequested)
                {
                    Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: запрашиваем версию фильтров");
                    foreach (var type in SupportedFilterTypes)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!uris.TryGetValue(type, out var uri))
                            continue;
                        Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: грузим фильтр {type} из {uri}");
                        var filterText = await Tools.Network.ReadTextAsync(uri, TimeSpan.FromSeconds(10), ct);
                        Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: фильтр {type} загружен");
                        var currentVersion = versions[type];
                        var newVersion = await TryUpdateFilter(currentVersion, type, filterText, true, ct);
                        if (newVersion > currentVersion)
                        {
                            string path = GetFilterFilePath(type);
                            var expandedPath = Model.StorageModel.ExpandRelativeName(path);
                            Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: фильтр {type} обновился, сохраняю в файл {path}");
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(expandedPath));
                                await Tools.File.WriteAllTextAsync(expandedPath, filterText);
                            }
                            catch (UnauthorizedAccessException e)
                            {
                                Tools.Logging.Send(LogLevel.Error, $"Управление фильтрами: недостаточно прав для сохранения в файл {path}", e);
                            }
                            catch (IOException e)
                            {
                                Tools.Logging.Send(LogLevel.Error, $"Управление фильтрами: не могу сохранить в файл {path}", e);
                            }
                        }
                        versions[type] = newVersion;
                    }
                    Tools.Logging.Send(LogLevel.Trace, "Управление фильтрами: итерация обновления окончена");

                    await Task.Delay(TimeSpan.FromMinutes(10), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                /* ничего не делаем, нас отменили */
            }
            Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: цикл обновления завершён");
        }

        async Task<int> TryUpdateFilter(int oldVersion, Model.Filter.Type type, string filterText, bool fresh, CancellationToken ct)
        {
            try
            {
                Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: разбираю фильтр {type}");

                async Task<int> RunUpdate<T>(Func<int, string, (T, int)> parser, Action<T> setter, EventHandler<EventArgs> filterUpdated)
                {
                    (var data, var version) = await Task.Run(() => parser(oldVersion, filterText), ct);
                    ct.ThrowIfCancellationRequested();
                    if (data == null || version <= oldVersion)
                    {
                        Tools.Logging.Send(LogLevel.Trace, $"Управление фильтрами: версия фильтра {type} не обновилась");
                        return oldVersion;
                    }
                    if (oldVersion < 0)
                        Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: загрузил фильтр {type} версии {version}");
                    else
                        Tools.Logging.Send(LogLevel.Info, $"Управление фильтрами: заменяю фильтр {type} на версию {version}");
                    setter(data);
                    Updated?.Invoke();
                    filterUpdated?.Invoke(this, EventArgs.Empty);
                    return version;
                }

                switch (type)
                {
                case Model.Filter.Type.Items:
                    return await RunUpdate(Model.FiltersModel.ParseItems, data => Model.FiltersModel.StoreItems(data, fresh), ItemsUpdated);
                case Model.Filter.Type.Planets:
                    return await RunUpdate(Model.FiltersModel.ParseSectors, data => Model.FiltersModel.StoreSectors(data, fresh), SectorsUpdated);
                case Model.Filter.Type.Missions:
                    return await RunUpdate(Model.FiltersModel.ParseMissions, data => Model.FiltersModel.StoreMissions(data, fresh), MissionsUpdated);
                case Model.Filter.Type.Factions:
                    return await RunUpdate(Model.FiltersModel.ParseFactions, data => Model.FiltersModel.StoreFactions(data, fresh), FactionsUpdated);
                case Model.Filter.Type.Builds:
                    return await RunUpdate(Model.FiltersModel.ParseBuilds, data => Model.FiltersModel.StoreBuilds(data, fresh), BuildsUpdated);
                case Model.Filter.Type.Planets_new:
                    return await RunUpdate(Model.FiltersModel.ParsePlanets, data => Model.FiltersModel.StorePlanets(data, fresh), PlanetsUpdated);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Tools.Logging.Send(LogLevel.Warn, $"Управление фильтрами: ошибка при разборе фильтра типа {type}", e);
                return oldVersion;
            }

            throw new NotSupportedException($"Управление фильтрами: не имплементирована обработка типа фильтров {type}");
        }
    }
}