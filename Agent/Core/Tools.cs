﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Core
{
    /// <summary>
    ///     Класс вспомогательных утилит.
    /// </summary>
    public class Tools
    {
        /// <summary>
        ///     Взаимодействия с интернетом.
        /// </summary>
        public class Network
        {
            #region Ping

            /// <summary>
            ///     Проверить адрес на доступность
            /// </summary>
            /// <param name="address">Адрес</param>
            /// <param name="timeout">Задержка</param>
            /// <returns>true/false</returns>
            public static bool Ping(Uri address, int timeout = 100000) //TODO: Нужна ли? Двойной запрос до сервера.
            {
                var statusCode = 0;
                if (address == null) return false;
                try
                {
                    var request = (HttpWebRequest) WebRequest.Create(address);
                    request.AllowAutoRedirect = true;
                    request.Timeout = timeout;
                    request.Method = WebRequestMethods.Http.Get;
                    request.Accept = @"*/*";
                    var response = (HttpWebResponse) request.GetResponse();
                    statusCode = (int) response.StatusCode;
                    response.Close();
                }
                catch (Exception e)
                {
                    Logging.Send(LogLevel.Warn, "Ping Error", e);
                }

                return statusCode == 200;
            }

            #endregion

            #region DownloadFile

            /// <summary>
            ///     Загружает файл в локальную директорию
            /// </summary>
            /// <param name="uri">Адрес файла</param>
            /// <param name="relativePath">Куда сохранять</param>
            public static void DownloadFile(Uri uri, string relativePath)
            {
                using (var c = new WebClient())
                {
                    c.DownloadFile(uri, Model.StorageModel.ExpandRelativeName(relativePath));
                }
            }

            #endregion

            #region ReadText

            public static string ReadText(Uri uri)
            {
                var wc = new WebClient {Encoding = Encoding.UTF8};
                return wc.DownloadString(uri);
            }

            public static async Task<string> ReadTextAsync(Uri uri, CancellationToken ct = default(CancellationToken))
            {
                var wc = new WebClient {Encoding = Encoding.UTF8};
                using (ct.Register(wc.CancelAsync))
                    return await wc.DownloadStringTaskAsync(uri);
            }

            public static async Task<string> ReadTextAsync(Uri uri, TimeSpan timeout, CancellationToken ct = default(CancellationToken))
            {
                var wc = new WebClient {Encoding = Encoding.UTF8};
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (cts.Token.Register(wc.CancelAsync))
                {
                    try
                    {
                        cts.CancelAfter(timeout);
                        return await wc.DownloadStringTaskAsync(uri);
                    }
                    catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        throw new TimeoutException("Couldn't read text within allotted time frame", ex);
                    }
                }
            }

            #endregion

            #region Send Bad Filter

            /// <summary>
            /// Отправляет Put запрос на указанный адрес
            /// </summary>
            /// <param name="data">Объект для сериализации в JSON</param>
            /// <param name="uri">Адрес для отправки</param>
            public static Task<bool> SendPut(object data, Uri uri = null)
            {
                return PutRequest(data, uri ?? new Uri("https://evgeniy-z.ru/api/v2/Agent/PutFilter"));
            }

            /// <summary>
            /// Отправляет Put запрос на указанный адрес
            /// </summary>
            /// <param name="name">Переменная для отрпавки</param>
            /// <param name="type">Тип (items, missions), соответсвует имени файла самого фильтра</param>
            /// <param name="version">Версия приложения</param>
            /// <param name="uri">Адрес для отправки</param>
            public static Task<bool> SendPut(string name, string type, string version, Uri uri = null)
            {
                var data = new { Name = name, Type = type, Version = version };
                return PutRequest(data, uri ?? new Uri("https://evgeniy-z.ru/api/v2/Agent/PutFilter"));
            }

            private static async Task<bool> PutRequest(object data, Uri uri)
            {
                string serializedObject = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                Logging.Send(LogLevel.Debug, $"Отправка на сервер объекта {serializedObject}");
                HttpWebRequest request = WebRequest.CreateHttp(uri);
                request.Method = "PUT";
                request.AllowWriteStreamBuffering = false;
                request.ContentType = "application/json";
                request.Accept = "Accept=application/json";
                request.SendChunked = false;
                request.ContentLength = serializedObject.Length;
                try
                {
                    var timeoutTask = Task.Delay(5000);
                    var putTask = PutWorker(request, serializedObject);

                    async Task<HttpStatusCode> PutWorker(HttpWebRequest req, string s)
                    {
                        using (var writer = new StreamWriter(await req.GetRequestStreamAsync()))
                        {
                            await writer.WriteAsync(s);
                            Logging.Send(LogLevel.Debug, "Отправка на сервер: запрос послан");
                        }
                        var response = (HttpWebResponse)(await req.GetResponseAsync());
                        return response.StatusCode;
                    }

                    var firstToFinish = await Task.WhenAny(timeoutTask, putTask);
                    if (firstToFinish == timeoutTask)
                    {
                        request.Abort();
                    }
                    else
                    {
                        var statusCode = await putTask;
                        switch (statusCode)
                        {
                        case HttpStatusCode.Created:
                        case HttpStatusCode.Conflict:
                        case (HttpStatusCode)422: //TODO: Проверить...
                            Logging.Send(LogLevel.Debug,
                                $"Отправка на сервер: получено подтверждение{(statusCode == HttpStatusCode.OK ? "" : " (фильтр уже добавлен)")}");
                            return true;
                        }

                        Logging.Send(LogLevel.Warn, $"Отправка на сервер: получен неожиданный код ответа {statusCode}");
                    }
                }
                catch (WebException e) when (e.Response is HttpWebResponse hwr && hwr.StatusCode == HttpStatusCode.Conflict)
                {
                    Logging.Send(LogLevel.Debug, "Отправка на сервер: получено подтверждение (фильтр уже добавлен)");
                    return true;
                }
                catch (Exception e)
                {
                    Logging.Send(LogLevel.Warn, "Отправка на сервер: произошло исключение", e);
                }
                return false;
            }

            #endregion
        }

        /// <summary>
        ///     Работа со временем.
        /// </summary>
        public class Time
        {
            #region ToDateTime

            /// <summary>
            ///     Переводит UnixTime(ms) в DateTime
            /// </summary>
            /// <param name="timestamp">Unix время в формате long</param>
            /// <returns>DateTime</returns>
            public static DateTime ToDateTime(long timestamp)
            {
                var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                dateTime = dateTime.AddMilliseconds(timestamp).ToLocalTime();
                return Convert.ToDateTime(dateTime);
            }

            #endregion

            #region ToUnixTime

            /// <summary>
            ///     Переводит DateTime в Unix (long)
            /// </summary>
            /// <param name="date">Время для перевода</param>
            /// <returns>Long</returns>
            public static long ToUnixTime(DateTime date)
            {
                var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                var diff = date.ToUniversalTime() - origin;
                return Convert.ToInt64(diff.TotalMilliseconds);
            }

            #endregion
        }

        public class Logging
        {
            private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
            public static void Send(LogLevel level, string message, Exception exception = null, object param = null)
            {
                string errorMsg = null;
                if (exception != null)
                    errorMsg = $"\n{exception}";

                Debug.WriteLine($"{DateTime.Now} | {level.Name.ToUpper()} | {message}{errorMsg}");
                Logger.Log(level, exception, message, param);

                if (level >= LogLevel.Warn)
                    Microsoft.HockeyApp.HockeyClient.Current.TrackEvent($"{message}{errorMsg}");
            }
        }

        /// <summary>
        ///   Асинхронная работа с потоками
        /// </summary>
        public class Async
        {
            public static ThreadPoolRedirector RedirectToThreadPool() =>
                default(ThreadPoolRedirector);

            public struct ThreadPoolRedirector : INotifyCompletion
            {
                public ThreadPoolRedirector GetAwaiter() => this;
                public bool IsCompleted => Thread.CurrentThread.IsThreadPoolThread;
                public void OnCompleted(Action continuation) => ThreadPool.QueueUserWorkItem(o => continuation());
                public void GetResult() { }
            }
        }

        /// <summary>
        ///   Асинхронная работа с файлами
        /// </summary>
        public class File
        {
            public static async Task WriteAllTextAsync(string path, string text)
            {
                using (var f = System.IO.File.CreateText(path))
                    await f.WriteAsync(text);
            }

            public static async Task<string> ReadAllTextAsync(string path)
            {
                using (var f = System.IO.File.OpenText(path))
                    return await f.ReadToEndAsync();
            }

            public static async Task<string> ReadAllTextAsync(string path, TimeSpan timeout, CancellationToken ct)
            {
                var timeoutTask = Task.Delay(timeout, ct);
                using (var f = System.IO.File.OpenText(path))
                {
                    var readTask = f.ReadToEndAsync();
                    var firstFinished = await Task.WhenAny(timeoutTask, readTask);
                    if (firstFinished == readTask)
                        return await readTask;
                    await timeoutTask;
                    throw new TimeoutException("Read operation didn't finish within allotted time");
                }
            }
        }
    }

    public static class KeyValuePairExtensions
    {
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> self, out K key, out V value)
        {
            key = self.Key;
            value = self.Value;
        }
    }
}
