﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Core.Model;

namespace Core.ViewModel
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
            var (planet, location) = Model.Filters.ExpandSector(sector);
            return new SectorViewModel(planet, location);
        }

        public string Planet { get; }
        public string Location { get; }
    }
}