﻿using FFXIV_GameSense.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using XIVDB;

namespace FFXIV_GameSense.UI
{
    public class FATEListViewItem : INotifyPropertyChanged
    {
        public ushort ID { get; private set; }
        public byte ClassJobLevel => GameResources.GetFATEInfo(ID).ClassJobLevel;
        public string Icon => FFXIVHunts.baseUrl + "images/" + GameResources.GetFATEInfo(ID).IconMap;
        public string Name => GameResources.GetFATEInfo(ID).Name;
        private string zones;
        public string Zones
        {
            get => zones ?? string.Empty;
            set
            {
                if (zones != value)
                {
                    zones = value;
                    OnPropertyChanged(nameof(Zones));
                }
            }
        }
        public bool Announce
        {
            get
            {
                return Settings.Default.FATEs.Contains(ID);
            }
            set
            {
                if (!value)
                    while (Settings.Default.FATEs.Contains(ID))
                    {
                        Settings.Default.FATEs.Remove(ID);
                        OnPropertyChanged(nameof(Announce));
                    }
                else if(!Settings.Default.FATEs.Contains(ID))
                {
                    Settings.Default.FATEs.Add(ID);
                    OnPropertyChanged(nameof(Announce));
                }
            }
        }

        public FATEListViewItem(FATE f)
        {
            ID = f.ID;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class FATEPresetViewItem
    {
        public string Name { get; private set; }
        public IEnumerable<ushort> FATEIDs { get; private set; }
        public FATEPresetViewItem(RelicNote book)
        {
            Name = book.BookName.FirstLetterToUpperCase();
            FATEIDs = book.FATEs.Select(x => x.ID);
        }
    }

    public class PerformanceListViewItem
    {
        public string RelativePath { get; set; }
        //public string FileName => Path.GetFileName(RelativePath);
        public DateTime LastModified { get; set; }
    }
}
