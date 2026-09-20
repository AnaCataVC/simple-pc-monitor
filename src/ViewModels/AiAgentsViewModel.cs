using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class AiAgentsViewModel : ViewModelBase
    {
        private AiAgentMetric _metric = new();
        private ObservableCollection<AiAgentSession> _sessions = new();
        private ObservableCollection<AiAgentMcpServer> _orphanProcesses = new();

        public AiAgentMetric Metric
        {
            get => _metric;
            set => SetProperty(ref _metric, value);
        }

        public ObservableCollection<AiAgentSession> Sessions
        {
            get => _sessions;
            set => SetProperty(ref _sessions, value);
        }

        public ObservableCollection<AiAgentMcpServer> OrphanProcesses
        {
            get => _orphanProcesses;
            set => SetProperty(ref _orphanProcesses, value);
        }

        public AsyncRelayCommand<AiAgentSession> CloseSessionCommand { get; }
        public AsyncRelayCommand<AiAgentMcpServer> KillOrphanCommand { get; }
        public AsyncRelayCommand KillAllOrphansCommand { get; }

        public event Action<string>? ShowToastRequested;

        public AiAgentsViewModel()
        {
            CloseSessionCommand = new AsyncRelayCommand<AiAgentSession>(async session =>
            {
                if (session == null) return;
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(session.ParentPid, session.AgentName));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), session.AgentName, res));
            });

            KillOrphanCommand = new AsyncRelayCommand<AiAgentMcpServer>(async orphan =>
            {
                if (orphan == null) return;
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(orphan.Pid, orphan.ProcessName));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), orphan.ProcessName, res));
            });

            KillAllOrphansCommand = new AsyncRelayCommand(async () =>
            {
                int killed = 0;
                await Task.Run(() =>
                {
                    foreach (var o in _orphanProcesses)
                    {
                        ProcessManager.RequestGracefulCloseAsync(o.Pid, o.ProcessName);
                        killed++;
                    }
                });
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastOrphansKilled"), killed));
            });
        }

        public void Update(AiAgentMetric metric)
        {
            Metric = metric;
            Sessions = new ObservableCollection<AiAgentSession>(metric.Sessions);
            OrphanProcesses = new ObservableCollection<AiAgentMcpServer>(metric.OrphanProcesses);
        }
    }
}