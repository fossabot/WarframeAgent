﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using Core;

namespace Agent.ViewModel
{
    class SplashViewModel
    {
        readonly MainViewModel mainVM;
        bool isFinished = false;

        public SplashViewModel(MainViewModel mainVM)
        {
            this.mainVM = mainVM;
        }

        public void Run()
        {
            mainVM.GameDataEvent.Updated += GameDataEvent_Updated;
            mainVM.GameDataEvent.Disconnected += GameDataEvent_Disconnected;

            var isConnected = mainVM.GameDataEvent.IsGameConnected;
            if (isConnected != null)
            {
                isFinished = true;
                mainVM.GameDataEvent.Updated -= GameDataEvent_Updated;
                mainVM.GameDataEvent.Disconnected -= GameDataEvent_Disconnected;

                if (isConnected == true)
                    OnConnected();
                else
                    OnDisconnected();
            }
        }

        public event EventHandler<SplashExitedEventArgs> Exited;

        #region События

        private async void GameDataEvent_Disconnected()
        {
            await AsyncHelpers.RedirectToMainThread();

            if (isFinished)
                return;
            isFinished = true;

            mainVM.GameDataEvent.Updated -= GameDataEvent_Updated;
            mainVM.GameDataEvent.Disconnected -= GameDataEvent_Disconnected;

            OnDisconnected();
        }

        private void OnDisconnected()
        {
            SplashExitedEventArgs exitArgs;

            if (File.Exists($"{Settings.Program.Directories.Temp}/GameData.json") &&
                File.Exists($"{Settings.Program.Directories.Temp}/NewsData.json"))
            {
                var message = MessageBox.Show(
                    "Невозможно получить данные с сервера.\nНо нам удалось найти старые данные.\nПоказать?",
                    "Внимание!",
                    MessageBoxButton.OKCancel);

                if (message == MessageBoxResult.OK)
                    exitArgs = new SplashExitedEventArgs(allowRun: true, hasConnection: false);
                else
                    exitArgs = new SplashExitedEventArgs(allowRun: false, hasConnection: false);
            }
            else
            {
                MessageBox.Show("Невозможно получить данные с сервера.");
                exitArgs = new SplashExitedEventArgs(allowRun: false, hasConnection: false);
            }

            Exited?.Invoke(this, exitArgs);
        }

        private async void GameDataEvent_Updated()
        {
            await AsyncHelpers.RedirectToMainThread();

            if (isFinished)
                return;
            isFinished = true;

            mainVM.GameDataEvent.Updated -= GameDataEvent_Updated;
            mainVM.GameDataEvent.Disconnected -= GameDataEvent_Disconnected;

            OnConnected();
        }

        private void OnConnected()
        {
            SplashExitedEventArgs exitArgs =  new SplashExitedEventArgs(allowRun: true, hasConnection: true);
            Exited?.Invoke(this, exitArgs);
        }
        #endregion
    }

    class SplashExitedEventArgs : EventArgs
    {
        public bool AllowApplicationRun { get; }
        public bool HasConnection { get; }
        public SplashExitedEventArgs(bool allowRun, bool hasConnection)
        {
            AllowApplicationRun = allowRun;
            HasConnection = hasConnection;
        }
    }
}
