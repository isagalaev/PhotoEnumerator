using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

using ExifLib;


namespace PhotoEnumerator
{
    public class PictureInfo
    {
        public string Name { get; set; }
        public DateTime Time { get; set; }
        public string Camera { get; set; }

        public PictureInfo(string filename)
        {
            Name = filename;
            using (var reader = new ExifReader(filename))
            {
                DateTime time;
                reader.GetTagValue<DateTime>(ExifTags.DateTimeDigitized, out time);
                Time = time;
                string make, model;
                reader.GetTagValue<string>(ExifTags.Make, out make);
                reader.GetTagValue<string>(ExifTags.Model, out model);
                if (model != null || make != null)
                {
                    Camera = String.Format("{0} {1}", make, model);
                }
            }
        }
    }

    public class Source
    {
        public List<PictureInfo> Pictures;

        public Source(IEnumerable<string> filenames)
        {
            Pictures = (from filename in filenames select new PictureInfo(filename)).ToList();
            Pictures.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        public string Title
        {
            get { return Path.GetDirectoryName(Pictures[0].Name); }
        }

        public string Camera
        {
            get { return Pictures[0].Camera ?? "<Unknown camera>"; }
        }

        public int Count
        {
            get { return Pictures.Count; }
        }

        public bool Contains(string filename)
        {
            return Pictures.Find(p => p.Name == filename) != null;
        }
    }

    public class Rename
    {
        public PictureInfo Picture { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public string TargetDir;

        public bool Conflict
        {
            get
            {
                return File.Exists(Path.Combine(TargetDir, NewName));
            }
        }
    }

    public class MainWindowViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<Source> Sources { get; set; }

        private string _TargetDir;
        public string TargetDir
        {
            get { return _TargetDir; }
            set
            {
                if (value == _TargetDir) return;
                _TargetDir = value;
                OnPropertyChanged("TargetDir");
                OnPropertyChanged("Renames");
            }
        }

        private string _Mask = "yyyy-MM-dd_";
        public string Mask
        {
            get { return _Mask; }
            set
            {
                if (value == _Mask) return;
                _Mask = value;
                OnPropertyChanged("Mask");
                OnPropertyChanged("Renames");
            }
        }

        private int _Counter = 1;
        public int Counter
        {
            get { return _Counter; }
            set
            {
                if (value == _Counter) return;
                _Counter = value;
                OnPropertyChanged("Counter");
                OnPropertyChanged("Renames");
            }
        }

        public IEnumerable<Rename> Renames
        {
            get
            {
                if (TargetDir == null) yield break;
                var pictures = Sources.SelectMany(s => s.Pictures).ToList();
                pictures.Sort((a, b) => a.Time.CompareTo(b.Time));
                var counter = Counter;
                foreach (var picture in pictures)
                {
                    yield return new Rename()
                    {
                        Picture = picture,
                        OldName = Path.GetFileName(picture.Name),
                        NewName = String.Format("{0}{1:D3}.jpg", picture.Time.ToString(Mask), counter),
                        TargetDir = TargetDir
                    };
                    counter++;
                }
            }
        }

        public bool Conflict
        {
            get { return Renames.Any(r => r.Conflict); }
        }

        public MainWindowViewModel()
        {
            Sources = new ObservableCollection<Source>();
            Sources.CollectionChanged += (sender, args) => { OnPropertyChanged("Sources"); OnPropertyChanged("Renames"); };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private MainWindowViewModel Data;

        public MainWindow()
        {
            InitializeComponent();
            Data = new MainWindowViewModel();
            DataContext = Data;
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Pictures|*.jpeg;*.jpg";
            dialog.Multiselect = true;
            if (dialog.ShowDialog() == true)
            {
                var filenames = from f in dialog.FileNames
                                where !Data.Sources.Any(s => s.Contains(f))
                                select f;
                if (filenames.Any())
                {
                    Data.Sources.Add(new Source(filenames));
                }
            }

        }

        private void btnTargetDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Data.TargetDir = dialog.FileName;
            }
        }
    }
}
