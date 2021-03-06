﻿using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Osmo.Core;
using Osmo.Core.Configuration;
using Osmo.Core.Objects;
using Osmo.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;
using System.Linq;
using Osmo.Core.Reader;
using MaterialDesignThemes.Wpf;
using System.Diagnostics;
using System.Windows.Input;
using System.Threading.Tasks;
using Osmo.Core.FileExplorer;
using Osmo.Core.Logging;

namespace Osmo.UI
{
    /// <summary>
    /// Interaction logic for SkinEditor.xaml
    /// </summary>
    public partial class SkinEditor : Grid, IHotkeyHelper, ISkinContainer
    {
        private static SkinEditor instance;
        private AudioEngine audio;

        private CompletionWindow completionWindow;

        private List<CompletionData> skinIniCompletion = new List<CompletionData>();

        public static SkinEditor Instance
        {
            get
            {
                if (instance == null)
                    instance = new SkinEditor();
                return instance;
            }
        }

        #region AudioPosition
        public double AudioPosition
        {
            get { return (double)GetValue(AudioPositionProperty); }
            set { SetValue(AudioPositionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for AudioPosition.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AudioPositionProperty =
            DependencyProperty.Register("AudioPosition", typeof(double), typeof(SkinEditor), new PropertyMetadata(0d));
        #endregion

        public Skin LoadedSkin { get => (DataContext as SkinViewModel).LoadedSkin; }

        //public double AudioPosition { get => audio != null ? audio.AudioPosition : 0; }

        public bool ForwardKeyboardInput(KeyEventArgs e)
        {
            SkinViewModel vm = DataContext as SkinViewModel;
            if (vm.IsFABEnabled)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    switch (e.Key)
                    {
                        case Key.H:
                            e.Handled = true;
                            Replace_Click(null, null);

                            Helper.ExecuteDialogOpenCommand(btn_replace);
                            DialogHelper.Instance.NotifyDialogOpened(btn_replace);
                            break;
                        case Key.Delete:
                            e.Handled = true;
                            Erase_Click(null, null);
                            break;
                        case Key.Z:
                            e.Handled = true;
                            if ((DataContext as SkinViewModel).ResetEnabled)
                            {
                                Revert_Click(null, null);
                            }
                            break;
                    }
                }
                else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.A)
                {
                    e.Handled = true;
                    if (vm.AnimationEnabled)
                    {
                        animationHelper.LoadAnimation((DataContext as SkinViewModel).SelectedElement);

                        Helper.ExecuteDialogOpenCommand(btn_animate);
                        DialogHelper.Instance.NotifyDialogOpened(btn_animate);
                    }
                }
                else if (e.Key == Key.Delete)
                {
                    e.Handled = true;
                    Delete_Click(null, null);
                }
            }

            return e.Handled;
        }

        private SkinEditor()
        {
            LoadCompletionData();
            InitializeComponent();
            audio = new AudioEngine((AudioViewModel)DataContext);
        }

        public void LoadCompletionData()
        {
            Logger.Instance.WriteLog("Refreshing completion data...");
            skinIniCompletion.Clear();
            skinIniCompletion.AddRange(FixedValues.skinIniGeneralCompletionData);
            skinIniCompletion.AddRange(FixedValues.skinIniColoursCompletionData);
            skinIniCompletion.AddRange(FixedValues.skinIniFontsCompletionData);
            skinIniCompletion.AddRange(FixedValues.skinIniCTBCompletionData);
            skinIniCompletion.AddRange(FixedValues.skinIniManiaCompletionData);
            Logger.Instance.WriteLog("Completion data refreshed!");
        }

        public async Task<bool> LoadSkin(Skin skin)
        {
            SkinViewModel vm = DataContext as SkinViewModel;
            
            if (LoadedSkin != null && !LoadedSkin.Equals(new Skin()) && LoadedSkin != skin && LoadedSkin.UnsavedChanges)
            {
                var msgBox = MaterialMessageBox.Show(Helper.FindString("main_unsavedChangesTitle"),
                       Helper.FindString("main_unsavedChangesDescription"),
                       OsmoMessageBoxButton.YesNoCancel);

                await DialogHelper.Instance.ShowDialog(msgBox);

                if (msgBox.Result == OsmoMessageBoxResult.Cancel)
                {
                    return false;
                }
                else if (msgBox.Result == OsmoMessageBoxResult.Yes)
                {
                    vm.LoadedSkin.Save();
                }
            }
            
            vm.LoadedSkin = skin;
            return true;
        }

        public void UnloadSkin(Skin skin)
        {
            SkinViewModel vm = DataContext as SkinViewModel;
            if (vm.LoadedSkin?.Equals(skin) ?? false)
            {
                StopAudio();
                vm.SelectedElement = new SkinElement();
                vm.LoadedSkin = null;
            }
        }

        public void SaveSkin()
        {
            ((SkinViewModel)DataContext).SaveSkin();
            
            snackbar.MessageQueue.Enqueue(Helper.FindString("snackbar_saveText"), 
                Helper.FindString("snackbar_saveButton"),
                param => DialogHost.Show(FindResource("folderPicker") as FilePicker), false, true);
        }

        public void ExportSkin(string targetDir, bool alsoSave)
        {
            if (alsoSave)
            {
                ((SkinViewModel)DataContext).SaveSkin();
            }

            ((SkinViewModel)DataContext).ExportSkin(targetDir, alsoSave);
            snackbar.MessageQueue.Enqueue(Helper.FindString("snackbar_exportText"),
                Helper.FindString("snackbar_exportButton"),
                param => Process.Start(targetDir), false, true);
        }

        private void Elements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SkinViewModel vm = (SkinViewModel)DataContext;

            if (lv_elements.SelectedIndex != -1)
                vm.SelectedElement = (SkinElement)lv_elements.SelectedItem;
            else
                vm.SelectedElement = new SkinElement();

            StopAudio();

            vm.ShowIcon = vm.SelectedElement.FileType == FileType.Image ? Visibility.Hidden : Visibility.Visible;
            if (vm.ShowIcon == Visibility.Visible)
            {
                switch (vm.SelectedElement.FileType)
                {
                    case FileType.Audio:
                        vm.Icon = PackIconKind.FileMusic;
                        break;
                    case FileType.Configuration:
                        vm.Icon = PackIconKind.FileXml;
                        LoadConfigFile(vm.SelectedElement.Path);
                        break;
                    case FileType.Any:
                        vm.Icon = PackIconKind.File;
                        break;
                }
            }

            vm.ShowEditor = vm.SelectedElement.Name.ToLower() == "skin.ini" ? Visibility.Visible : Visibility.Hidden;
        }

        private void LoadConfigFile(string path)
        {
            textEditor.Document = null; // immediately remove old document
            TextDocument doc = new TextDocument(new StringTextSource(File.ReadAllText(path)));
            doc.SetOwnerThread(Application.Current.Dispatcher.Thread);
            Application.Current.Dispatcher.BeginInvoke(
                  new Action(
                      delegate
                      {
                          textEditor.Document = doc;
                      }), DispatcherPriority.Normal);
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            PreloadFilePickerProperties(Helper.FindString("edit_replaceTitle"),
                (DataContext as SkinViewModel).SelectedElement.FileType,
                "replace");
        }

        private void PreloadFilePickerProperties(string title, FileType fileType, string tag)
        {
            FilePicker fp = FindResource("filePicker") as FilePicker;
            fp.Title = title;
            fp.FilterType = fileType;
            fp.Tag = tag;
        }

        private void FilePicker_DialogClosed(object sender, RoutedEventArgs e)
        {
            if (sender is FilePicker fp) {
                FilePickerClosedEventArgs args = e as FilePickerClosedEventArgs;

                if (args.Path != null)
                {
                    SkinViewModel vm = DataContext as SkinViewModel;

                    switch (fp.Tag)
                    {
                        case "replace":
                            vm.SelectedElement.ReplaceBackup(new FileInfo(args.Path));
                            StopAudio();
                            vm.RefreshImage();

                            vm.ResetEnabled = true;
                            break;
                    }
                }
            }
        }

        private async void Revert_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = MaterialMessageBox.Show(Helper.FindString("edit_revertTitle"),
                Helper.FindString("edit_revertDescription"),
                OsmoMessageBoxButton.YesNo);

            await DialogHelper.Instance.ShowDialog(msgBox);

            if (msgBox.Result == OsmoMessageBoxResult.Yes)
            {
                SkinViewModel vm = (SkinViewModel)DataContext;
                vm.SelectedElement.Reset();
                /*string path = AppConfiguration.GetInstance().BackupDirectory + "\\" + 
                    vm.LoadedSkin.Name + "\\";
                File.Copy(path + vm.SelectedElement.Name, vm.SelectedElement.Path, true);*/
                StopAudio();
                vm.RefreshImage();
                vm.ResetEnabled = false;
            }
        }

        private async void Erase_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = MaterialMessageBox.Show(Helper.FindString("edit_eraseTitle"),
                Helper.FindString("edit_eraseDescription"),
                OsmoMessageBoxButton.YesNo);

            await DialogHelper.Instance.ShowDialog(msgBox);

            string path = ((SkinViewModel)DataContext).SelectedElement.ReplaceBackup(null);

            if (msgBox.Result == OsmoMessageBoxResult.Yes)
            {
                SkinElement current = ((SkinViewModel)DataContext).SelectedElement;
                StopAudio();
                ElementGenerator.Generate(current.ReplaceBackup(null), current.ElementDetails.IsSound);
                ((SkinViewModel)DataContext).RefreshImage();
                ((SkinViewModel)DataContext).ResetEnabled = true;
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = MaterialMessageBox.Show(Helper.FindString("edit_deleteTitle"),
                Helper.FindString("edit_deleteDescription"),
                OsmoMessageBoxButton.YesNo);

            await DialogHelper.Instance.ShowDialog(msgBox);

            if (msgBox.Result == OsmoMessageBoxResult.Yes)
            {
                SkinElement element = ((SkinViewModel)DataContext).SelectedElement;
                if (lv_elements.SelectedIndex < lv_elements.Items.Count - 1)
                    lv_elements.SelectedIndex++;
                else
                    lv_elements.SelectedIndex--;
                element.Delete();
                StopAudio();
                ((SkinViewModel)DataContext).ResetEnabled = false;
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (audio.PlayAudio((DataContext as SkinViewModel).SelectedElement.Path))
            {
                if (cb_mute.IsChecked == true)
                    audio.SetVolume(0);
                else
                    audio.SetVolume(slider_volume.Value);
            }
            else
            {
                (DataContext as SkinViewModel).PlayStatus = 0;
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            SkinViewModel vm = DataContext as SkinViewModel;

            if (vm.AudioEnded)
            {
                audio.PlayAudio(vm.SelectedElement.Path);
            }
            else
            {
                audio.PauseAudio();
                vm.PlayStatus = 0;
            }
        }

        private void Slider_volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (audio != null)
            {
                cb_mute.IsChecked = false;
                RecallConfiguration.Instance.Volume = slider_volume.Value;
                audio.SetVolume(slider_volume.Value);
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            RecallConfiguration.Instance.IsMuted = cb_mute.IsChecked == true;
            if (cb_mute.IsChecked == true)
                audio.SetVolume(0);
            else
                audio.SetVolume(slider_volume.Value);
        }

        private void TextEditor_Loaded(object sender, RoutedEventArgs e)
        {
            using (Stream s = Application.GetResourceStream(new Uri("pack://application:,,,/Osmo;component/Resources/SkinIniSyntax.xshd", UriKind.Absolute)).Stream)
            {
                using (XmlTextReader reader = new XmlTextReader(s))
                {
                    textEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }

            textEditor.TextArea.TextEntering += TextArea_TextEntering;
            textEditor.TextArea.TextEntered += TextArea_TextEntered;
        }

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if ((DataContext as SkinViewModel).SelectedElement.Name.Equals("skin.ini", 
                StringComparison.InvariantCultureIgnoreCase))
            {
                //TODO: More intelligent code completion by using the group which is above the current line (for example [Colours])
                completionWindow = new CompletionWindow(textEditor.TextArea);
                IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;
                string line = Helper.GetTextAtCurrentLine(textEditor);
                foreach (CompletionData item in skinIniCompletion)
                {
                    if (item.Text.Contains(line))
                        data.Add(item);
                }

                if (data.Count > 0)
                {
                    completionWindow.Show();
                    completionWindow.Closed += delegate { completionWindow = null; };
                }
            }
            else
            {
                completionWindow = null;
            }
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (completionWindow != null && !char.IsLetterOrDigit(e.Text[0]))
            {
                // Whenever a non-letter is typed while the completion window is open,
                // insert the currently selected element.
                completionWindow.CompletionList.RequestInsertion(e);
            }
        }

        private void Container_Loaded(object sender, RoutedEventArgs e)
        {
            slider_volume.Value = RecallConfiguration.Instance.Volume;
            cb_mute.IsChecked = RecallConfiguration.Instance.IsMuted;
        }

        private void Slider_Audio_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue % 1 != 0)
            {
                audio.SetPosition(slider_audio.Value);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopAudio();
        }

        private void StopAudio()
        {
            (DataContext as SkinViewModel).PlayStatus = 0;
            audio.StopAudio();
        }

        private async void ChangeList_Revert_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = MaterialMessageBox.Show(Helper.FindString("edit_revertTitle"),
                Helper.FindString("edit_revertDescription"),
                OsmoMessageBoxButton.YesNo);

            await DialogHelper.Instance.ShowDialog(msgBox);

            if (msgBox.Result == OsmoMessageBoxResult.Yes)
            {
                SkinViewModel vm = (SkinViewModel)DataContext;
                SkinElement element = vm.LoadedSkin.Elements.FirstOrDefault(x => x.Name.Equals(
                    (sender as Button).Tag)) ?? null;

                if (element != null)
                {
                    element.Reset();
                    if (element.Equals(vm.SelectedElement))
                    {
                        vm.RefreshImage();
                        vm.ResetEnabled = false;
                    }
                }
            }
        }

        private void Animate_Click(object sender, RoutedEventArgs e)
        {
            animationHelper.LoadAnimation((DataContext as SkinViewModel).SelectedElement);
            DialogHelper.Instance.NotifyDialogOpened(btn_animate);
        }

        private void container_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            ForwardKeyboardInput(e);

            //if (e.Key == Key.A && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            //{
            //    MessageBox.Show("Show animation helper!");
            //}
            //else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            //{
            //    MessageBox.Show("Replace with another file!");
            //}
        }

        private void FolderPicker_DialogClosed(object sender, RoutedEventArgs e)
        {
            FilePickerClosedEventArgs args = e as FilePickerClosedEventArgs;

            if (args.Path != null)
            {
                Helper.ExportSkin(args.Path, FixedValues.EDITOR_INDEX, true);
            }
        }

        private void FilePicker_DialogOpened(object sender, RoutedEventArgs e)
        {
            (sender as FilePicker).InitialDirectory = (DataContext as SkinViewModel).LoadedSkin.Path;
        }

        private void MenuItem_Replace_Click(object sender, RoutedEventArgs e)
        {
            Replace_Click(sender, e);

            Helper.ExecuteDialogOpenCommand(btn_replace);
            DialogHelper.Instance.NotifyDialogOpened(btn_replace);
        }

        private void MenuItem_Animate_Click(object sender, RoutedEventArgs e)
        {
            Animate_Click(sender, e);

            Helper.ExecuteDialogOpenCommand(btn_animate);
            DialogHelper.Instance.NotifyDialogOpened(btn_animate);
        }

        private void MenuItem_Play_Click(object sender, RoutedEventArgs e)
        {
            Play_Click(sender, e);
            (DataContext as SkinViewModel).PlayStatus = 1;
        }

        private void MenuItem_Pause_Click(object sender, RoutedEventArgs e)
        {
            Pause_Click(sender, e);
            (DataContext as SkinViewModel).PlayStatus = 0;
        }
    }
}
