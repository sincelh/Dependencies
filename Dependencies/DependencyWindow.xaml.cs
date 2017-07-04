﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.ClrPh;
using System.ComponentModel;




public class DefaultSettingsBindingHandler : INotifyPropertyChanged
{
    public delegate string CallbackEventHandler(bool settingValue);
    public struct EventHandlerInfo
    {
        public string Property;
        public string Settings;
        public string MemberBindingName;
        public CallbackEventHandler Handler;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private List<EventHandlerInfo> Handlers;

    public DefaultSettingsBindingHandler()
    {
        Dependencies.Properties.Settings.Default.PropertyChanged += this.Handler_PropertyChanged;
        Handlers = new List<EventHandlerInfo>();
    }

    public void AddNewEventHandler(string PropertyName, string SettingsName, string MemberBindingName, CallbackEventHandler Handler )
    {
        EventHandlerInfo info = new EventHandlerInfo();
        info.Property = PropertyName;
        info.Settings = SettingsName;
        info.MemberBindingName = MemberBindingName;
        info.Handler = Handler;

        Handlers.Add(info);
    }

    public virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Handler_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        foreach (EventHandlerInfo Handler in Handlers.FindAll(x => x.Property == e.PropertyName))
        {
            Handler.Handler(((bool)Dependencies.Properties.Settings.Default[Handler.Settings]));
            OnPropertyChanged(Handler.MemberBindingName);
        }

        
    }
}


public struct TreeViewItemContext
{
    // union-like
    public PE PeProperties; // null if not found
    public PeImportDll ImportProperties;

    public string ModuleName;
    public string PeFilePath; // null if not found

    public List<PeExport> PeExports; // null if not found
    public List<PeImportDll> PeImports; // null if not found
}

namespace Dependencies
{
    public class ModuleTreeViewItem : TreeViewItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ModuleTreeViewItem()
        {
            Dependencies.Properties.Settings.Default.PropertyChanged += this.ModuleTreeViewItem_PropertyChanged;
        }

        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string GetTreeNodeHeaderName(bool FullPath)
        {
            TreeViewItemContext Context = ((TreeViewItemContext)DataContext);

            if ((FullPath) && (Context.PeFilePath != null))
            {
                return Context.PeFilePath;
            }
            else
            {
                return Context.ModuleName;
            }
        }

    
        private void ModuleTreeViewItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "FullPath")
            {
                this.Header = (object) GetTreeNodeHeaderName(Dependencies.Properties.Settings.Default.FullPath);
            }
        }
    }


  


    /// <summary>
    /// Logique d'interaction pour DependencyWindow.xaml
    /// </summary>
    public partial class DependencyWindow : UserControl
    {
        PE Pe;
        string RootFolder;
        PhSymbolProvider SymPrv;
        HashSet<String> ModulesFound;
        HashSet<String> ModulesNotFound;

        public List<TreeViewItemContext> ProcessPe(PE newPe)
        {
            List<TreeViewItemContext> NewTreeContexts = new List<TreeViewItemContext>();
            List<PeImportDll> PeImports = newPe.GetImports();

            foreach (PeImportDll DllImport in PeImports)
            {

                // Find Dll in "paths"
                String PeFilePath = FindPe.FindPeFromDefault(DllImport.Name, RootFolder, this.Pe.IsWow64Dll());
                PE ImportPe = (PeFilePath != null) ? new PE(PeFilePath) : null;


                if (PeFilePath == null)
                {
                    this.ModulesNotFound.Add(DllImport.Name);
                }
                   
                TreeViewItemContext childTreeInfoContext = new TreeViewItemContext();
                childTreeInfoContext.PeProperties = ImportPe;
                childTreeInfoContext.ImportProperties = DllImport;
                childTreeInfoContext.PeFilePath = PeFilePath;
                childTreeInfoContext.ModuleName = DllImport.Name;

                NewTreeContexts.Add(childTreeInfoContext);
            }
            
            return NewTreeContexts;

        }

        private void ConstructDependencyTree(ModuleTreeViewItem RootNode, PE CurrentPE, int RecursionLevel = 0)
        {
            List<Tuple<ModuleTreeViewItem, PE>> BacklogPeToProcess = new List<Tuple<ModuleTreeViewItem, PE>>();
            
            foreach ( TreeViewItemContext NewTreeContext in ProcessPe(CurrentPE))
            {
                ModuleTreeViewItem childTreeNode = new ModuleTreeViewItem();

                // Missing module found
                if (this.ModulesNotFound.Contains(NewTreeContext.ModuleName))
                {
                    this.ModulesList.Items.Add(new DisplayErrorModuleInfo(NewTreeContext.ImportProperties));
                }
                else
                {
                    if (!this.ModulesFound.Contains(NewTreeContext.PeFilePath))
                    {
                        // do not process twice the same PE in order to lessen memory pressure
                        BacklogPeToProcess.Add(new Tuple<ModuleTreeViewItem, PE>(childTreeNode, NewTreeContext.PeProperties));
                    }


                    this.ModulesFound.Add(NewTreeContext.PeFilePath);
                    this.ModulesList.Items.Add(new DisplayModuleInfo(NewTreeContext.ImportProperties, NewTreeContext.PeProperties));
                }

                // Add to tree view
                childTreeNode.DataContext = NewTreeContext;
                childTreeNode.Header = childTreeNode.GetTreeNodeHeaderName(Dependencies.Properties.Settings.Default.FullPath);
                RootNode.Items.Add(childTreeNode);
            }



            // Process next batch of dll imports
            foreach (Tuple<ModuleTreeViewItem, PE> NewPeNode in BacklogPeToProcess)
            {
                ConstructDependencyTree(NewPeNode.Item1, NewPeNode.Item2, RecursionLevel + 1); // warning : recursive call
            }
        }


        public DependencyWindow(String FileName)
        {

            InitializeComponent();

            this.Pe = new PE(FileName);
            this.RootFolder = Path.GetDirectoryName(FileName);
            this.SymPrv = new PhSymbolProvider();
            this.ModulesFound = new HashSet<String>();
            this.ModulesNotFound = new HashSet<String>();

            this.ModulesList.Items.Clear();
            this.DllTreeView.Items.Clear();

            ModuleTreeViewItem treeNode = new ModuleTreeViewItem();
            TreeViewItemContext childTreeInfoContext = new TreeViewItemContext();

            childTreeInfoContext.PeProperties = this.Pe;
            childTreeInfoContext.ImportProperties = null;
            childTreeInfoContext.PeFilePath = this.Pe.Filepath;
            childTreeInfoContext.ModuleName = FileName;

            treeNode.DataContext = childTreeInfoContext;
            treeNode.Header = treeNode.GetTreeNodeHeaderName(Dependencies.Properties.Settings.Default.FullPath);
            treeNode.IsExpanded = true;
            
            this.DllTreeView.Items.Add(treeNode);

            // Recursively construct tree of dll imports
            ConstructDependencyTree(treeNode, this.Pe);
        }

        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            TreeViewItemContext childTreeContext = ((TreeViewItemContext)(this.DllTreeView.SelectedItem as ModuleTreeViewItem).DataContext);

            PE SelectedPE = childTreeContext.PeProperties;

            this.ImportList.Items.Clear();
            this.ExportList.Items.Clear();

            // Selected Pe has not been found on disk
            if (SelectedPE == null)
                return;

            // Process imports and exports on first load
            if (childTreeContext.PeExports == null) { childTreeContext.PeExports = SelectedPE.GetExports(); }
            if (childTreeContext.PeImports == null) { childTreeContext.PeImports = SelectedPE.GetImports(); }

                
            
            foreach (PeImportDll DllImport in childTreeContext.PeImports)
            {
                String PeFilePath = FindPe.FindPeFromDefault(DllImport.Name, RootFolder, this.Pe.IsWow64Dll());

                foreach (PeImport Import in DllImport.ImportList)
                {
                    this.ImportList.Items.Add(new DisplayPeImport(Import, SymPrv, PeFilePath));
                }
            }

            foreach (PeExport Export in childTreeContext.PeExports)
            {
                this.ExportList.Items.Add(new DisplayPeExport(Export, SymPrv));
            }

        }
    }
}
