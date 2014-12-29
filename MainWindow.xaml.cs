using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.IO;
using Microsoft.Win32;

using ExifLib;


namespace PhotoEnumerator
{
    public class PictureInfo
    {
        public string Name;
        public DateTime Time;
        public string Camera;

        public PictureInfo(string filename)
        {
            Name = filename;
            using (var reader = new ExifReader(filename))
            {
                reader.GetTagValue<DateTime>(ExifTags.DateTimeDigitized, out Time);
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
    }

    public partial class MainWindow : Window
    {

        private ObservableCollection<Source> sources = new ObservableCollection<Source>();

        private IEnumerable<Rename> Renames(string format, int counter)
        {
            foreach (var source in sources)
            {
                foreach (var picture in source.Pictures)
                {
                    yield return new Rename()
                    {
                        Picture = picture,
                        OldName = Path.GetFileName(picture.Name),
                        NewName = String.Format("{0}{1:D3}.jpg", picture.Time.ToString(format), counter)
                    };
                    counter++;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            icSources.ItemsSource = sources;
            sources.CollectionChanged += sources_CollectionChanged;
        }

        void sources_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            lvTarget.ItemsSource = Renames("yyyy-MM-dd_", 1);
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Pictures|*.jpeg;*.jpg";
            dialog.Multiselect = true;
            if (dialog.ShowDialog() == true) 
            {
                var filenames = from f in dialog.FileNames
                                where !sources.Any(s => s.Contains(f))
                                select f;
                if (filenames.Any())
                {
                    sources.Add(new Source(filenames));
                }
            }

        }

    }
}
