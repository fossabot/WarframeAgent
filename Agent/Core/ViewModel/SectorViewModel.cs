﻿namespace Core.ViewModel
{
    public class SectorViewModel
    {
        private SectorViewModel(string planet, string location)
        {
            Planet = planet;
            Location = location;
        }

        public static SectorViewModel FromSector(string sector)
        {
            var sectorFilter = Model.Filters.ExpandSector(sector);
            return new SectorViewModel(sectorFilter?.Planet, sectorFilter?.Location ?? sector);
        }

        public string Planet { get; }
        public string Location { get; }
    }
}
