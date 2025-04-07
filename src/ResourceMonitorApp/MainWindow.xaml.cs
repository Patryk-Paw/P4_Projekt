// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Windows.Shapes;

namespace SystemResourceMonitor
{
    public partial class MainWindow : Window
    {
        private PerformanceCounter cpuCounter;
        private PerformanceCounter ramCounter;
        private PerformanceCounter diskReadCounter;
        private PerformanceCounter diskWriteCounter;
        private PerformanceCounter networkSentCounter;
        private PerformanceCounter networkReceivedCounter;

        private List<double> cpuReadings = new List<double>();
        private List<double> ramReadings = new List<double>();
        private List<double> diskReadReadings = new List<double>();
        private List<double> diskWriteReadings = new List<double>();
        private List<double> networkSentReadings = new List<double>();
        private List<double> networkReceivedReadings = new List<double>();

        private DispatcherTimer timer;
        private const int MaxDataPoints = 60; 

        // Process monitoring fields
        private ObservableCollection<ProcessInfo> processes = new ObservableCollection<ProcessInfo>();
        private Dictionary<int, PerformanceCounter> processCpuCounters = new Dictionary<int, PerformanceCounter>();
        private DispatcherTimer processUpdateTimer;
        private ICollectionView processesView;

        public MainWindow()
        {
            InitializeComponent();
            InitializePerformanceCounters();
            InitializeTimer();
            InitializeProcessMonitoring();
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

                string networkInterface = GetActiveNetworkInterface();
                if (!string.IsNullOrEmpty(networkInterface))
                {
                    networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface);
                    networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing performance counters: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetActiveNetworkInterface()
        {
            PerformanceCounterCategory category = new PerformanceCounterCategory("Network Interface");
            string[] instanceNames = category.GetInstanceNames();

            foreach (string name in instanceNames)
            {
                using (PerformanceCounter counter = new PerformanceCounter("Network Interface", "Bytes Total/sec", name))
                {
                    if (counter.NextValue() > 0)
                    {
                        return name;
                    }
                    Thread.Sleep(100);
                    if (counter.NextValue() > 0)
                    {
                        return name;
                    }
                }
            }

            // If no active interface found, return the first one (if any)
            return instanceNames.Length > 0 ? instanceNames[0] : null;
        }

        private void InitializeTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdatePerformanceData();
            UpdateUI();
        }

        private void UpdatePerformanceData()
        {
            try
            {
                // Get current values
                double cpuUsage = Math.Round(cpuCounter.NextValue(), 1);
                double ramUsage = Math.Round(ramCounter.NextValue(), 1);
                double diskReadBytes = diskReadCounter.NextValue();
                double diskWriteBytes = diskWriteCounter.NextValue();

                // Add to collections
                AddToLimitedCollection(cpuReadings, cpuUsage);
                AddToLimitedCollection(ramReadings, ramUsage);
                AddToLimitedCollection(diskReadReadings, ConvertBytesToMB(diskReadBytes));
                AddToLimitedCollection(diskWriteReadings, ConvertBytesToMB(diskWriteBytes));

                // Update network (if available)
                if (networkSentCounter != null && networkReceivedCounter != null)
                {
                    double networkSent = networkSentCounter.NextValue();
                    double networkReceived = networkReceivedCounter.NextValue();

                    AddToLimitedCollection(networkSentReadings, ConvertBytesToMB(networkSent));
                    AddToLimitedCollection(networkReceivedReadings, ConvertBytesToMB(networkReceived));
                }
            }
            catch (Exception ex)
            {
                // Log error 
                Console.WriteLine($"Error updating performance data: {ex.Message}");
            }
        }

        private void AddToLimitedCollection(List<double> collection, double value)
        {
            collection.Add(value);
            if (collection.Count > MaxDataPoints)
            {
                collection.RemoveAt(0);
            }
        }

        private double ConvertBytesToMB(double bytes)
        {
            return Math.Round(bytes / (1024 * 1024), 2);
        }

        private void UpdateUI()
        {
            // Update CPU
            cpuValueText.Text = $"{cpuReadings.LastOrDefault():F1}%";
            cpuProgressBar.Value = cpuReadings.LastOrDefault();
            UpdateChart(cpuChart, cpuReadings, Colors.DodgerBlue);

            // Update RAM
            ramValueText.Text = $"{ramReadings.LastOrDefault():F1}%";
            ramProgressBar.Value = ramReadings.LastOrDefault();
            UpdateChart(ramChart, ramReadings, Colors.LimeGreen);

            // Update Disk
            diskReadValueText.Text = $"{diskReadReadings.LastOrDefault():F2} MB/s";
            diskWriteValueText.Text = $"{diskWriteReadings.LastOrDefault():F2} MB/s";
            UpdateChart(diskChart, diskReadReadings, Colors.Orange);
            UpdateChart(diskWriteChart, diskWriteReadings, Colors.Red);

            // Update Network
            if (networkSentReadings.Any() && networkReceivedReadings.Any())
            {
                networkUploadValueText.Text = $"{networkSentReadings.LastOrDefault():F2} MB/s";
                networkDownloadValueText.Text = $"{networkReceivedReadings.LastOrDefault():F2} MB/s";
                UpdateChart(networkUploadChart, networkSentReadings, Colors.Purple);
                UpdateChart(networkDownloadChart, networkReceivedReadings, Colors.Teal);
            }
        }

        private void UpdateChart(Canvas canvas, List<double> data, Color color)
        {
            canvas.Children.Clear();

            if (data.Count <= 1)
                return;


            double maxValue = 100;
            if (canvas != cpuChart && canvas != ramChart)
            {
                maxValue = data.Max() > 0 ? data.Max() * 1.2 : 1; 
            }

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double xStep = width / (MaxDataPoints - 1);

            Polyline polyline = new Polyline();
            polyline.Stroke = new SolidColorBrush(color);
            polyline.StrokeThickness = 1.5;

            for (int i = 0; i < data.Count; i++)
            {
                double x = i * xStep;
                double y = height - (Math.Min(data[i], maxValue) / maxValue * height);
                polyline.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(polyline);
        }

        // Process Monitoring Methods

        public class ProcessInfo : INotifyPropertyChanged
        {
            private string _name;
            private int _id;
            private double _cpuUsage;
            private long _memoryUsage;
            private string _status;

            public string Name
            {
                get => _name;
                set
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }

            public int Id
            {
                get => _id;
                set
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }

            public double CpuUsage
            {
                get => _cpuUsage;
                set
                {
                    _cpuUsage = value;
                    OnPropertyChanged(nameof(CpuUsage));
                    OnPropertyChanged(nameof(CpuUsageFormatted));
                }
            }

            public string CpuUsageFormatted => $"{CpuUsage:F1}%";

            public long MemoryUsage
            {
                get => _memoryUsage;
                set
                {
                    _memoryUsage = value;
                    OnPropertyChanged(nameof(MemoryUsage));
                    OnPropertyChanged(nameof(MemoryUsageFormatted));
                }
            }

            public string MemoryUsageFormatted => $"{MemoryUsage / (1024 * 1024):F1} MB";

            public string Status
            {
                get => _status;
                set
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void InitializeProcessMonitoring()
        {
            processesView = CollectionViewSource.GetDefaultView(processes);
            processesListView.ItemsSource = processesView;

            processUpdateTimer = new DispatcherTimer();
            processUpdateTimer.Interval = TimeSpan.FromSeconds(2);
            processUpdateTimer.Tick += ProcessUpdateTimer_Tick;
            processUpdateTimer.Start();

            RefreshProcessList();
        }

        private void ProcessUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateProcessInfo();
        }

        private void RefreshProcessList()
        {
            try
            {
                // Clear existing processes and counters
                processes.Clear();
                foreach (var counter in processCpuCounters.Values)
                {
                    counter.Dispose();
                }
                processCpuCounters.Clear();

                // Get all processes
                Process[] runningProcesses = Process.GetProcesses();

                foreach (Process process in runningProcesses)
                {
                    try
                    {
                        var processInfo = new ProcessInfo
                        {
                            Name = process.ProcessName,
                            Id = process.Id,
                            Status = "Running"
                        };

                        // Try to get memory info
                        try
                        {
                            processInfo.MemoryUsage = process.WorkingSet64;
                        }
                        catch
                        {
                            processInfo.MemoryUsage = 0;
                        }

                        // Try to create CPU counter
                        try
                        {
                            var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName, true);
                            cpuCounter.NextValue(); // First call always returns 0
                            processCpuCounters[process.Id] = cpuCounter;
                        }
                        catch
                        {
                            // Some system processes may not allow counters
                        }

                        processes.Add(processInfo);
                    }
                    catch
                    {
                        // We Skip processes that we can't access
                    }
                }

                // Sort by memory usage (descending)
                processesView.SortDescriptions.Clear();
                processesView.SortDescriptions.Add(new SortDescription("MemoryUsage", ListSortDirection.Descending));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing process list: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateProcessInfo()
        {
            try
            {
                // Get current processes
                var currentProcesses = Process.GetProcesses().ToDictionary(p => p.Id);

                // Update existing processes
                for (int i = processes.Count - 1; i >= 0; i--)
                {
                    var processInfo = processes[i];

                    // Check if process still exists
                    if (currentProcesses.TryGetValue(processInfo.Id, out Process process))
                    {
                        try
                        {
                            // Update memory usage
                            processInfo.MemoryUsage = process.WorkingSet64;

                            // Update CPU usage if counter exists
                            if (processCpuCounters.TryGetValue(processInfo.Id, out PerformanceCounter counter))
                            {
                                try
                                {
                                    processInfo.CpuUsage = Math.Round(counter.NextValue() / Environment.ProcessorCount, 1);
                                }
                                catch
                                {
                                    processInfo.CpuUsage = 0;
                                }
                            }
                        }
                        catch
                        {
                            processes.RemoveAt(i);
                        }
                    }
                    else
                    {
                        if (processCpuCounters.TryGetValue(processInfo.Id, out PerformanceCounter counter))
                        {
                            counter.Dispose();
                            processCpuCounters.Remove(processInfo.Id);
                        }
                        processes.RemoveAt(i);
                    }
                }

                // Check for new processes
                var existingIds = new HashSet<int>(processes.Select(p => p.Id));
                foreach (var process in currentProcesses.Values)
                {
                    if (!existingIds.Contains(process.Id))
                    {
                        try
                        {
                            var processInfo = new ProcessInfo
                            {
                                Name = process.ProcessName,
                                Id = process.Id,
                                MemoryUsage = process.WorkingSet64,
                                Status = "Running"
                            };

                            // Try to create CPU counter
                            try
                            {
                                var cpuCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName, true);
                                cpuCounter.NextValue(); 
                                processCpuCounters[process.Id] = cpuCounter;
                            }
                            catch
                            {
                                // Some system processes may not allow counters
                            }

                            processes.Add(processInfo);
                        }
                        catch
                        {
                            // We Skip processes that we can't access
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating process info: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
        }

        private void EndProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (processesListView.SelectedItem is ProcessInfo selectedProcess)
            {
                try
                {
                    Process process = Process.GetProcessById(selectedProcess.Id);
                    if (MessageBox.Show($"Are you sure you want to end the process '{selectedProcess.Name}' (ID: {selectedProcess.Id})?",
                                       "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        process.Kill();
                        selectedProcess.Status = "Terminating";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error ending process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select a process to end.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            timer.Stop();
            processUpdateTimer?.Stop();

            // Dispose performance counters
            cpuCounter?.Dispose();
            ramCounter?.Dispose();
            diskReadCounter?.Dispose();
            diskWriteCounter?.Dispose();
            networkSentCounter?.Dispose();
            networkReceivedCounter?.Dispose();

            foreach (var counter in processCpuCounters.Values)
            {
                counter.Dispose();
            }
        }
    }
}