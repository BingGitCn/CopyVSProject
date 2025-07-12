using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks; // For async/await
using System.Windows; // For MessageBox
using System.Windows.Forms; // For FolderBrowserDialog
using System.Windows.Input;
using CopyVSProject.Views; // Add this line for ConfirmationDialog

namespace CopyVSProject.ViewModels
{
    /// <summary>
    /// MainWindow的视图模型，负责处理UI逻辑和数据绑定。
    /// </summary>
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string _sourcePath;
        /// <summary>
        /// 获取或设置源文件夹路径。
        /// </summary>
        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                _sourcePath = value;
                OnPropertyChanged(nameof(SourcePath));
                // 当源路径改变时，重新评估压缩命令的可用性。
                ((RelayCommand)CompressProjectCommand).RaiseCanExecuteChanged();
                // 当源路径改变时，清空输出路径，以便用户重新选择。
                OutputPath = string.Empty;
            }
        }

        private string _outputPath;
        /// <summary>
        /// 获取或设置输出文件路径（ZIP文件）。
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                OnPropertyChanged(nameof(OutputPath));
                // 当输出路径改变时，重新评估压缩命令的可用性。
                ((RelayCommand)CompressProjectCommand).RaiseCanExecuteChanged();
            }
        }

        private string _currentOperationStatus;
        /// <summary>
        /// 获取或设置当前操作的状态信息（例如“正在复制文件...”）。
        /// </summary>
        public string CurrentOperationStatus
        {
            get => _currentOperationStatus;
            set
            {
                _currentOperationStatus = value;
                OnPropertyChanged(nameof(CurrentOperationStatus));
                // 当当前操作状态改变时，触发StatusMessage的更新。
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        /// <summary>
        /// 获取显示给用户的状态消息，它直接反映CurrentOperationStatus。
        /// </summary>
        public string StatusMessage
        {
            get => CurrentOperationStatus;
        }

        private bool _isCompressing;
        /// <summary>
        /// 获取或设置一个值，指示压缩操作是否正在进行。
        /// </summary>
        public bool IsCompressing
        {
            get => _isCompressing;
            set
            {
                _isCompressing = value;
                OnPropertyChanged(nameof(IsCompressing));
                // 当压缩状态改变时，重新评估压缩命令的可用性。
                ((RelayCommand)CompressProjectCommand).RaiseCanExecuteChanged();
            }
        }

        // 用于统计处理的文件和目录数量。
        private int _processedFilesCount;
        private int _processedDirectoriesCount;
        private int _ignoredFilesCount;
        private int _ignoredDirectoriesCount;

        /// <summary>
        /// 获取选择源文件夹的命令。
        /// </summary>
        public ICommand SelectSourceFolderCommand { get; }
        /// <summary>
        /// 获取选择输出文件的命令。
        /// </summary>
        public ICommand SelectOutputFileCommand { get; }
        /// <summary>
        /// 获取执行压缩项目的命令。
        /// </summary>
        public ICommand CompressProjectCommand { get; }

        /// <summary>
        /// MainWindowViewModel的构造函数。
        /// 初始化命令和默认状态。
        /// </summary>
        public MainWindowViewModel()
        {
            SelectSourceFolderCommand = new RelayCommand(SelectSourceFolder);
            SelectOutputFileCommand = new RelayCommand(SelectOutputFile);
            CompressProjectCommand = new RelayCommand(async (param) => await CompressProject(param), CanCompressProject);
            CurrentOperationStatus = "准备就绪。";
            IsCompressing = false;
        }

        /// <summary>
        /// 允许用户选择一个源文件夹。
        /// </summary>
        /// <param name="parameter">命令参数（未使用）。</param>
        private void SelectSourceFolder(object parameter)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择Visual Studio项目文件夹";
                dialog.ShowNewFolderButton = false;
                dialog.SelectedPath = SourcePath; // 设置初始路径（如果可用）

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    SourcePath = dialog.SelectedPath;
                }
            }
        }

        /// <summary>
        /// 允许用户选择一个输出ZIP文件路径，并根据源文件夹名称设置默认文件名。
        /// </summary>
        /// <param name="parameter">命令参数（未使用）。</param>
        private void SelectOutputFile(object parameter)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Zip Files (*.zip)|*.zip",
                DefaultExt = ".zip",
                Title = "保存项目压缩包为"
            };

            // 如果已选择源路径，则根据源文件夹名称设置默认文件名。
            if (!string.IsNullOrWhiteSpace(SourcePath))
            {
                string folderName = Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    saveFileDialog.FileName = folderName + ".zip";
                }
                else
                {
                    saveFileDialog.FileName = "ProjectArchive.zip";
                }
            }
            else
            {
                saveFileDialog.FileName = "ProjectArchive.zip";
            }

            if (saveFileDialog.ShowDialog() == true)
            {
                OutputPath = saveFileDialog.FileName;
            }
        }

        /// <summary>
        /// 判断压缩命令是否可以执行。
        /// </summary>
        /// <param name="parameter">命令参数（未使用）。</param>
        /// <returns>如果可以压缩，则为true；否则为false。</returns>
        private bool CanCompressProject(object parameter)
        {
            // 检查必要路径是否为空，源文件夹是否存在，以及是否正在进行压缩。
            if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(OutputPath) || !Directory.Exists(SourcePath) || IsCompressing)
            {
                return false;
            }

            // 检查输出路径是否在源路径内部，以防止递归压缩。
            try
            {
                string normalizedSourcePath = Path.GetFullPath(SourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string outputDirectory = Path.GetDirectoryName(OutputPath);
                if (outputDirectory == null) return false; // 对于有效文件路径不应发生。
                string normalizedOutputDirectory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (normalizedOutputDirectory.StartsWith(normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    // 如果输出目录与源目录相同，则允许。
                    // 否则，如果是子目录，则不允许。
                    return string.Equals(normalizedOutputDirectory, normalizedSourcePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                // 处理潜在的无效路径异常。
                return false;
            }

            return true;
        }

        /// <summary>
        /// 执行异步压缩项目操作。
        /// </summary>
        /// <param name="parameter">命令参数（未使用）。</param>
        private async Task CompressProject(object parameter)
        {
            // 压缩前的确认对话框。
            var confirmationDialog = new ConfirmationDialog("确定要开始压缩项目吗？");
            bool? result = confirmationDialog.ShowDialog();

            if (result != true)
            {
                CurrentOperationStatus = "压缩已取消。";
                return; // 用户取消操作。
            }

            IsCompressing = true;
            CurrentOperationStatus = "正在压缩项目...";
            // Progress<string>用于从后台线程向UI线程报告状态更新。
            var progress = new Progress<string>(message => CurrentOperationStatus = message);

            try
            {
                await Task.Run(() =>
                {
                    // 初始化计数器。
                    _processedFilesCount = 0;
                    _processedDirectoriesCount = 0;
                    _ignoredFilesCount = 0;
                    _ignoredDirectoriesCount = 0;

                    string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    ((IProgress<string>)progress).Report("创建临时目录...");
                    Directory.CreateDirectory(tempDirectory);

                    // 复制过滤后的文件和目录到临时目录。
                    CopyFilteredFiles(SourcePath, tempDirectory, progress);

                    ((IProgress<string>)progress).Report("创建ZIP压缩包... 请耐心等待。");
                    // 如果输出文件已存在，则删除。
                    if (File.Exists(OutputPath))
                    {
                        File.Delete(OutputPath);
                    }
                    // 从临时目录创建ZIP文件。
                    ZipFile.CreateFromDirectory(tempDirectory, OutputPath, CompressionLevel.Optimal, false);

                    ((IProgress<string>)progress).Report("清理临时目录...");
                    // 删除临时目录及其内容。
                    DeleteDirectoryRobustly(tempDirectory);

                    //// 构建并显示压缩总结信息。
                    //string finalStatus = "压缩完成！";
                    //string summary = $"\n总结: 成功复制 {(_processedFilesCount + _processedDirectoriesCount)} 项 (文件: {_processedFilesCount}, 目录: {_processedDirectoriesCount})。";
                    //if (_ignoredFilesCount > 0 || _ignoredDirectoriesCount > 0)
                    //{
                    //    summary += $" 忽略 {(_ignoredFilesCount + _ignoredDirectoriesCount)} 项 (文件: {_ignoredFilesCount}, 目录: {_ignoredDirectoriesCount})。";
                    //}
                    //((IProgress<string>)progress).Report(finalStatus + summary);
                });
            }
            catch (Exception ex)
            {
                // 捕获并报告任何在压缩过程中发生的异常。
                CurrentOperationStatus = $"错误: {ex.Message}";
            }
            finally
            {
                // 构建并显示压缩总结信息。
                string finalStatus = "压缩完成！";
                string summary = $"\n总结: 成功复制 {(_processedFilesCount + _processedDirectoriesCount)} 项 (文件: {_processedFilesCount}, 目录: {_processedDirectoriesCount})。";
                if (_ignoredFilesCount > 0 || _ignoredDirectoriesCount > 0)
                {
                    summary += $" 忽略 {(_ignoredFilesCount + _ignoredDirectoriesCount)} 项 (文件: {_ignoredFilesCount}, 目录: {_ignoredDirectoriesCount})。";
                }
                    ((IProgress<string>)progress).Report(finalStatus + summary);
                IsCompressing = false;
            }
        }

        /// <summary>
        /// 强制删除目录，包括移除只读属性和重试逻辑。
        /// </summary>
        /// <param name="directoryPath">要删除的目录路径。</param>
        private void DeleteDirectoryRobustly(string directoryPath)
        {
            const int maxRetries = 5;
            const int delayOnRetry = 100; // ms
            Exception lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var directory = new DirectoryInfo(directoryPath);
                    if (!directory.Exists) return;

                    // 移除所有文件的只读属性
                    foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
                    {
                        if (file.IsReadOnly)
                        {
                            file.IsReadOnly = false;
                        }
                    }

                    // 现在可以安全删除目录了
                    directory.Delete(true);
                    return; // Success
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    lastException = ex;
                    // 文件可能被锁定或权限问题，等待后重试
                    if (i < maxRetries - 1)
                    {
                        System.Threading.Thread.Sleep(delayOnRetry);
                    }
                }
            }

            // 如果所有重试都失败了
            if (lastException != null)
            {
                // 在UI线程上报告最终错误
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentOperationStatus = $"清理临时文件时出错: {lastException.Message}";
                });
            }
        }

        /// <summary>
        /// 递归复制源目录中符合条件的文件和目录到目标目录，并报告进度。
        /// </summary>
        /// <param name="sourceDir">源目录路径。</param>
        /// <param name="destinationDir">目标目录路径。</param>
        /// <param name="progress">用于报告进度的IProgress实例。</param>
        private void CopyFilteredFiles(string sourceDir, string destinationDir, IProgress<string> progress)
        {
            // 要忽略的目录列表。
            string[] ignoredDirectories = { "bin", "obj", ".vs", "packages", "node_modules" };
            // 无论在何处，都应忽略的文件扩展名。
            string[] alwaysIgnoredExtensions = { ".suo", ".user", ".cache", ".log", ".tmp" };

            ((IProgress<string>)progress).Report("正在复制目录...");
            // 遍历源目录中的所有子目录。
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, dirPath);
                bool shouldIgnore = false;

                // 检查目录路径的任何部分是否在忽略列表中。
                string[] pathComponents = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string component in pathComponents)
                {
                    if (shouldIgnore) break;
                    foreach (string ignoredDir in ignoredDirectories)
                    {
                        if (component.Equals(ignoredDir, StringComparison.OrdinalIgnoreCase))
                        {
                            shouldIgnore = true;
                            break;
                        }
                    }
                }

                if (shouldIgnore)
                {
                    ((IProgress<string>)progress).Report($"忽略目录: {relativePath}");
                    _ignoredDirectoriesCount++; // 统计被忽略的目录。
                    continue;
                }

                string destDirPath = dirPath.Replace(sourceDir, destinationDir);
                try
                {
                    ((IProgress<string>)progress).Report($"创建目录: {destDirPath}");
                    Directory.CreateDirectory(destDirPath);
                    _processedDirectoriesCount++; // 统计成功创建的目录。
                }
                catch (UnauthorizedAccessException ex)
                {
                    ((IProgress<string>)progress).Report($"错误: 无法创建目录 '{relativePath}' - 权限不足. {ex.Message}");
                    _ignoredDirectoriesCount++; // 统计因权限问题被忽略的目录。
                    continue;
                }
                catch (IOException ex)
                {
                    ((IProgress<string>)progress).Report($"错误: 无法创建目录 '{relativePath}' - {ex.Message}");
                    _ignoredDirectoriesCount++; // 统计因IO问题被忽略的目录。
                    continue;
                }
            }

            ((IProgress<string>)progress).Report("正在复制文件...");
            // 遍历源目录中的所有文件。
            foreach (string newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDir, newPath);
                bool isInsideIgnoredDir = false;

                // 检查文件是否在忽略的目录中。
                string[] pathComponents = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string component in pathComponents)
                {
                    if (isInsideIgnoredDir) break;
                    foreach (string ignoredDir in ignoredDirectories)
                    {
                        if (component.Equals(ignoredDir, StringComparison.OrdinalIgnoreCase))
                        {
                            isInsideIgnoredDir = true;
                            break;
                        }
                    }
                }

                bool shouldIgnore = false;
                if (isInsideIgnoredDir)
                {
                    shouldIgnore = true;
                }
                else
                {
                    // 如果文件不在被忽略的目录中，则只检查那些总是需要被忽略的扩展名。
                    // 这样可以保留那些不在bin/obj目录下的重要.dll文件。
                    string extension = Path.GetExtension(newPath);
                    foreach (string ignoredExt in alwaysIgnoredExtensions)
                    {
                        if (extension.Equals(ignoredExt, StringComparison.OrdinalIgnoreCase))
                        {
                            shouldIgnore = true;
                            break;
                        }
                    }
                }

                if (shouldIgnore)
                {
                    ((IProgress<string>)progress).Report($"忽略文件: {relativePath}");
                    _ignoredFilesCount++; // 统计被忽略的文件。
                    continue;
                }

                string destFilePath = newPath.Replace(sourceDir, destinationDir);
                try
                {
                    ((IProgress<string>)progress).Report($"复制文件: {relativePath}");
                    File.Copy(newPath, destFilePath, true);
                    _processedFilesCount++; // 统计成功复制的文件。
                }
                catch (UnauthorizedAccessException ex)
                {
                    ((IProgress<string>)progress).Report($"错误: 无法复制文件 '{relativePath}' - 权限不足. {ex.Message}");
                    _ignoredFilesCount++; // 统计因权限问题被忽略的文件。
                    continue;
                }
                catch (IOException ex)
                {
                    ((IProgress<string>)progress).Report($"错误: 无法复制文件 '{relativePath}' - {ex.Message}");
                    _ignoredFilesCount++; // 统计因IO问题被忽略的文件。
                    continue;
                }
            }
        }

        /// <summary>
        /// 当属性值改变时触发PropertyChanged事件。
        /// </summary>
        /// <param name="propertyName">发生改变的属性名称。</param>
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// RelayCommand是一个通用的ICommand实现，用于将UI命令绑定到视图模型方法。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        /// <summary>
        /// 当命令的CanExecute状态发生改变时触发。
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// 初始化RelayCommand的新实例。
        /// </summary>
        /// <param name="execute">要执行的Action。</param>
        /// <param name="canExecute">判断命令是否可以执行的Func（可选）。</param>
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 判断命令是否可以执行。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        /// <returns>如果可以执行，则为true；否则为false。</returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// 强制重新评估命令的CanExecute状态。
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}