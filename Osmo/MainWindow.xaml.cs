﻿using MahApps.Metro.Controls;
using MaterialDesignThemes.Wpf;
using Osmo.Core;
using Osmo.Core.Configuration;
using Osmo.Core.Objects;
using Osmo.Core.Reader;
using Osmo.UI;
using Osmo.ViewModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Osmo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        AppConfiguration configuration = AppConfiguration.GetInstance();

        public MainWindow()
        {
            InitializeComponent();
            configuration.SettingsSaved += Configuration_SettingsSaved;
            FixedValues.InitializeReader();
            SkinCreationWizard.Instance.ApplyMasterViewModel(DataContext as OsmoViewModel);
            
            if (!Directory.Exists(AppConfiguration.GetInstance().TemplateDirectory))
            {
                Directory.CreateDirectory(AppConfiguration.GetInstance().TemplateDirectory);
                File.WriteAllText(AppConfiguration.GetInstance().TemplateDirectory + "Default template.oft", 
                    Properties.Resources.DefaultTemplate);
            }
        }

        private void Configuration_SettingsSaved(object sender, EventArgs e)
        {
            OsmoViewModel vm = DataContext as OsmoViewModel;

            vm.BackupDirectory = configuration.BackupDirectory;
            vm.OsuDirectory = configuration.OsuDirectory;
        }

        private void UIElement_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
        }

        private void sidebarMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            (SkinEditor.Instance.DataContext as SkinViewModel).SelectedElement = new SkinElement();

            if (sidebarMenu.SelectedIndex != FixedValues.CONFIG_INDEX && !configuration.IsValid)
            {
                sidebarMenu.SelectedIndex = FixedValues.CONFIG_INDEX;
            }


            //until we had a StaysOpen glag to Drawer, this will help with scroll bars
            var dependencyObject = Mouse.Captured as DependencyObject;
            while (dependencyObject != null)
            {
                if (dependencyObject is ScrollBar) return;
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }

            MenuToggleButton.IsChecked = false;
        }

        private void sidebarMenu_Loaded(object sender, RoutedEventArgs e)
        {
            if (!configuration.IsValid)
                sidebarMenu.SelectedIndex = FixedValues.CONFIG_INDEX;

            dialg_newSkin.SetMasterViewModel(DataContext as OsmoViewModel);
        }

        private async void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            
            configuration.Save();
            (SkinEditor.Instance.animationHelper.DataContext as AnimationViewModel).StopAnimation();

            OsmoViewModel vm = DataContext as OsmoViewModel;
            SkinViewModel skinVm = SkinEditor.Instance.DataContext as SkinViewModel;
            if (skinVm.UnsavedChanges)
            {
                vm.SelectedSidebarIndex = FixedValues.EDITOR_INDEX;
                var msgBox = MaterialMessageBox.Show("You have unsaved changes!",
                    "You have unsaved changes! Do you want to save before closing? (Your changes will be remembered if you choose No)",
                    MessageBoxButton.YesNoCancel);

                await DialogHost.Show(msgBox);

                if (msgBox.Result == MessageBoxResult.Cancel)
                {
                    return;
                }
                else if (msgBox.Result == MessageBoxResult.Yes)
                {
                    skinVm.LoadedSkin.Save();
                }
            }

            SkinMixerViewModel mixerVm = SkinMixer.Instance.DataContext as SkinMixerViewModel;
            if (mixerVm.UnsavedChanges)
            {
                vm.SelectedSidebarIndex = FixedValues.MIXER_INDEX;
                var msgBox = MaterialMessageBox.Show("You have unsaved changes!",
                    "You have unsaved changes! Do you want to save before closing? (Your changes will be remembered if you choose No)",
                    MessageBoxButton.YesNoCancel);

                await DialogHost.Show(msgBox);

                if (msgBox.Result == MessageBoxResult.Cancel)
                {
                    return;
                }
                else if (msgBox.Result == MessageBoxResult.Yes)
                {
                    mixerVm.SkinLeft.Save();
                }
            }

            Environment.Exit(0);
        }

        private void SaveSkin_Click(object sender, RoutedEventArgs e)
        {
            if (sidebarMenu.SelectedIndex == 2)
            {
                SkinEditor.Instance.SaveSkin();
            }
            else if (sidebarMenu.SelectedIndex == 3)
            {
                SkinMixer.Instance.SaveSkin();
            }
        }

        private async void ExportSkin_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = MaterialMessageBox.Show("Save changes first?",
                "Do you wish to save your skin first?",
                MessageBoxButton.YesNoCancel);

            await DialogHost.Show(msgBox);

            if (msgBox.Result != MessageBoxResult.Cancel)
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog()
                {
                    Description = "Select the directory you want to export your skin to"
                })
                {
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        if (sidebarMenu.SelectedIndex == 2)
                        {
                            SkinEditor.Instance.ExportSkin(dlg.SelectedPath, msgBox.Result == MessageBoxResult.Yes);
                        }
                        else if (sidebarMenu.SelectedIndex == 3)
                        {
                            SkinMixer.Instance.ExportSkin(dlg.SelectedPath, msgBox.Result == MessageBoxResult.Yes);
                        }
                    }
                }
            }
        }

        private void OpenInFileExplorer_OnClick(object sender, RoutedEventArgs e)
        {
            OsmoViewModel vm = DataContext as OsmoViewModel;
            if (vm.SelectedSidebarIndex == FixedValues.EDITOR_INDEX)
            {
                Process.Start((SkinEditor.Instance.DataContext as SkinViewModel).LoadedSkin.Path);
            }
            else if (vm.SelectedSidebarIndex == FixedValues.MIXER_INDEX)
            {
                Process.Start((SkinMixer.Instance.DataContext as SkinMixerViewModel).SkinLeft.Path);
            }
        }

        private void RevertAll_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Do you really want to revert all changes made to this skin? This cannot be undone!",
                "Are you sure?", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                OsmoViewModel vm = DataContext as OsmoViewModel;
                if (vm.SelectedSidebarIndex == FixedValues.EDITOR_INDEX)
                {
                    (SkinEditor.Instance.DataContext as SkinViewModel).LoadedSkin.RevertAll();
                }
                else if (vm.SelectedSidebarIndex == FixedValues.MIXER_INDEX)
                {
                    (SkinMixer.Instance.DataContext as SkinMixerViewModel).SkinLeft.RevertAll();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MetroWindow_DragEnter(object sender, DragEventArgs e)
        {
            if (DialogHost.OpenDialogCommand.CanExecute(btn_import.CommandParameter, null))
                DialogHost.OpenDialogCommand.Execute(btn_import.CommandParameter, null);
        }

        private void OpenInSkinMixer_Click(object sender, RoutedEventArgs e)
        {
            SkinMixer.Instance.LoadSkin((SkinEditor.Instance.DataContext as SkinViewModel).LoadedSkin, true);
            (DataContext as OsmoViewModel).SelectedSidebarIndex = FixedValues.MIXER_INDEX;
        }

        private void OpenInSkinEditor_Click(object sender, RoutedEventArgs e)
        {
            SkinEditor.Instance.LoadSkin((SkinMixer.Instance.DataContext as SkinMixerViewModel).SkinLeft);
            (DataContext as OsmoViewModel).SelectedSidebarIndex = FixedValues.EDITOR_INDEX;
        }
    }
}
