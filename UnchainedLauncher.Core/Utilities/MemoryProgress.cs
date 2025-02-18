using PropertyChanged;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UnchainedLauncher.Core.Utilities {
    [AddINotifyPropertyChangedInterface]
    public partial class MemoryProgress : IProgress<double>, INotifyPropertyChanged {
        public double ProgressPercentage { get; protected set; }
        public string TaskName { get; protected set; }

        public MemoryProgress(string? taskName = null) {
            TaskName = taskName ?? "";
        }

        public void Report(double value) {
            ProgressPercentage = Math.Clamp(value, 0.00, 100.00);
        }
    }

    public class AccumulatedMemoryProgress : MemoryProgress {
        // average the progress of sub-percentages
        private double CalcAggregatePercentage() =>
            !Progresses.Any()
                ? 100.00
                : Progresses.ToList()
                      .Sum(mp => mp.ProgressPercentage)
                  / Progresses.Count;

        public ObservableCollection<MemoryProgress> Progresses { get; private set; }

        public AccumulatedMemoryProgress(IEnumerable<MemoryProgress>? memoryProgress = null, string? taskName = null) : base(taskName) {
            Progresses = new ObservableCollection<MemoryProgress>(memoryProgress ?? new List<MemoryProgress>());
            Progresses.ToList().ForEach(BindTo);
            ProgressPercentage = CalcAggregatePercentage();
        }

        public void AlsoTrack(MemoryProgress memoryProgress) {
            BindTo(memoryProgress);
            Progresses.Add(memoryProgress);
            ProgressPercentage = CalcAggregatePercentage();
        }

        private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(MemoryProgress.ProgressPercentage)) {
                ProgressPercentage = CalcAggregatePercentage();
            }
        }

        private void BindTo(MemoryProgress mp) {
            mp.PropertyChanged += OnProgressPropertyChanged;
        }
    }
}