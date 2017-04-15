﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickLook.Plugin.PDFViewer
{
    /// <summary>
    ///     Interaction logic for PdfViewer.xaml
    /// </summary>
    public partial class PdfViewerControl : UserControl, INotifyPropertyChanged, IDisposable
    {
        private PreviewMouseWheelMonitor _whellMonitor;

        public PdfViewerControl()
        {
            InitializeComponent();

            DataContext = this;

            PageIds = new ObservableCollection<int>();
        }

        public ObservableCollection<int> PageIds { get; set; }

        public PdfFile PdfHandleForThumbnails { get; private set; }

        public PdfFile PdfHandle { get; private set; }

        public bool PdfLoaded { get; private set; }

        public double ZoomFactor { get; set; }

        public int TotalPages => PdfHandle.TotalPages;

        public int CurrectPage
        {
            get => listThumbnails.SelectedIndex;
            set
            {
                listThumbnails.SelectedIndex = value;
                listThumbnails.ScrollIntoView(listThumbnails.SelectedItem);

                CurrentPageChanged?.Invoke(this, new EventArgs());
            }
        }

        public void Dispose()
        {
            _whellMonitor.Dispose();
            PdfHandleForThumbnails?.Dispose();
            PdfHandle?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler CurrentPageChanged;

        private void NavigatePage(object sender, MouseWheelEventArgs e)
        {
            if (!PdfLoaded)
                return;

            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (e.Delta > 0) // up
            {
                if (pageViewPanel.VerticalOffset != 0) return;

                PrevPage();
                e.Handled = true;
            }
            else // down
            {
                if (pageViewPanel.VerticalOffset != pageViewPanel.ScrollableHeight) return;

                NextPage();
                e.Handled = true;
            }
        }

        private void NextPage()
        {
            if (CurrectPage < PdfHandle.TotalPages - 1)
            {
                CurrectPage++;
                pageViewPanel.ScrollToTop();
            }
        }

        private void PrevPage()
        {
            if (CurrectPage > 0)
            {
                CurrectPage--;
                pageViewPanel.ScrollToBottom();
            }
        }

        private void ReRenderCurrentPageLowQuality(double viewZoom, bool fromCenter)
        {
            if (pageViewPanelImage.Source == null)
                return;

            var position = fromCenter
                ? new Point(pageViewPanelImage.Source.Width / 2, pageViewPanelImage.Source.Height / 2)
                : Mouse.GetPosition(pageViewPanelImage);

            pageViewPanelImage.LayoutTransform = new ScaleTransform(viewZoom, viewZoom);

            // critical for calcuating offset
            pageViewPanel.ScrollToHorizontalOffset(0);
            pageViewPanel.ScrollToVerticalOffset(0);
            UpdateLayout();

            var offset = pageViewPanelImage.TranslatePoint(position, pageViewPanel) - Mouse.GetPosition(pageViewPanel);
            pageViewPanel.ScrollToHorizontalOffset(offset.X);
            pageViewPanel.ScrollToVerticalOffset(offset.Y);
            UpdateLayout();
        }


        private void ReRenderCurrentPage()
        {
            if (!PdfLoaded)
                return;

            var image = PdfHandle.GetPage(CurrectPage, ZoomFactor).ToBitmapSource();

            pageViewPanelImage.Source = image;
            pageViewPanelImage.Width = pageViewPanelImage.Source.Width;
            pageViewPanelImage.Height = pageViewPanelImage.Source.Height;

            // reset view zoom factor
            pageViewPanelImage.LayoutTransform = new ScaleTransform();

            GC.Collect();
        }

        private void UpdatePageViewWhenSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!PdfLoaded)
                return;

            if (CurrectPage == -1)
                return;

            ReRenderCurrentPage();
        }

        private void ZoomToFit()
        {
            if (!PdfLoaded)
                return;

            var size = PdfHandle.GetPageSize(CurrectPage, 1d);

            var factor = Math.Min(pageViewPanel.ActualWidth / size.Width, pageViewPanel.ActualHeight / size.Height);

            ZoomFactor = factor;

            ReRenderCurrentPage();
        }

        public Size GetDesiredControlSizeByFirstPage(string path)
        {
            var tempHandle = new PdfFile(path);

            var size = tempHandle.GetPageSize(0, 1d);
            tempHandle.Dispose();

            size.Width += /*listThumbnails.ActualWidth*/ 150 + 1;

            return size;
        }

        public void LoadPdf(string path)
        {
            PageIds.Clear();
            _whellMonitor?.Dispose();

            PdfHandleForThumbnails = new PdfFile(path);
            PdfHandle = new PdfFile(path);
            PdfLoaded = true;

            // fill thumbnails list
            Enumerable.Range(0, PdfHandle.TotalPages).ForEach(PageIds.Add);
            OnPropertyChanged(nameof(PageIds));

            CurrectPage = 0;

            // calculate zoom factor for first page
            ZoomToFit();

            // register events
            listThumbnails.SelectionChanged += UpdatePageViewWhenSelectionChanged;
            //pageViewPanel.SizeChanged += ReRenderCurrentPageWhenSizeChanged;
            pageViewPanel.PreviewMouseWheel += NavigatePage;
            StartMouseWhellDelayedZoomMonitor(pageViewPanel);
        }

        private void StartMouseWhellDelayedZoomMonitor(UIElement ui)
        {
            if (_whellMonitor == null)
                _whellMonitor = new PreviewMouseWheelMonitor(ui, 100);

            var newZoom = 1d;
            var scrolling = false;

            _whellMonitor.PreviewMouseWheelStarted += (sender, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                    return;

                newZoom = ZoomFactor;
                scrolling = true;
            };
            _whellMonitor.PreviewMouseWheel += (sender, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                    return;

                e.Handled = true;

                newZoom = newZoom + e.Delta / 120 * 0.1;

                newZoom = Math.Max(newZoom, 0.2);
                newZoom = Math.Min(newZoom, 3);

                ReRenderCurrentPageLowQuality(newZoom / ZoomFactor, false);
            };
            _whellMonitor.PreviewMouseWheelStopped += (sender, e) =>
            {
                if (!scrolling)
                    return;

                ZoomFactor = newZoom;
                ReRenderCurrentPage();
            };
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}