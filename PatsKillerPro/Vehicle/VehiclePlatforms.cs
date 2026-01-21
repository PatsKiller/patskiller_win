using System.Collections.Generic;

namespace PatsKillerPro.Vehicle
{
    /// <summary>
    /// Ford/Lincoln vehicle platform definitions
    /// </summary>
    public static class VehiclePlatforms
    {
        private static readonly List<VehiclePlatform> _vehicles = new()
        {
            // Ford Trucks
            new VehiclePlatform { DisplayName = "Ford F-150 14th Gen (2021-Current)", Platform = "P702", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford F-150 13th Gen (2015-2020)", Platform = "P552", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Ranger (2022-Current)", Platform = "P703", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Ranger (2011-2022)", Platform = "P375", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Maverick (2022-Current)", Platform = "P758", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            
            // Ford SUVs
            new VehiclePlatform { DisplayName = "Ford Expedition (2018-Current)", Platform = "P702", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Explorer (2020-Current)", Platform = "U625", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Edge (2015-2024)", Platform = "CD539", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Escape (2020-Current)", Platform = "CX482", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Bronco (2021-Current)", Platform = "U725", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Bronco Sport (2021-Current)", Platform = "CX430", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford EcoSport (2012-2023)", Platform = "B2E", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            
            // Ford Cars
            new VehiclePlatform { DisplayName = "Ford Fusion (2013-2020)", Platform = "CD4", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Mondeo (2014-2022)", Platform = "CD4", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Focus Gen 4 (2018-2025)", Platform = "C519", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Focus Gen 3 (2011-2018)", Platform = "C346", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Fiesta Gen 7 (2017-2023)", Platform = "B479", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Fiesta Gen 6 (2008-2017)", Platform = "B299", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            
            // Ford Commercial
            new VehiclePlatform { DisplayName = "Ford Transit (2014-Current)", Platform = "V363", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Transit Connect (2014-2023)", Platform = "V408", Brand = "Ford", SupportsKeyless = false, HsCanBaud = 500000 },
            
            // Ford Europe
            new VehiclePlatform { DisplayName = "Ford Kuga (2020-Current)", Platform = "CX482", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford S-Max (2015-2023)", Platform = "CD4", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Galaxy (2015-2023)", Platform = "CD4", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Ford Puma (2019-Current)", Platform = "B479", Brand = "Ford", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            
            // Lincoln SUVs
            new VehiclePlatform { DisplayName = "Lincoln Navigator (2018-Current)", Platform = "P702", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln Aviator (2020-Current)", Platform = "U611", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln Nautilus (2019-2024)", Platform = "CD539", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln MKX (2015-2018)", Platform = "CD539", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln Corsair (2020-Current)", Platform = "CX483", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln MKC (2015-2019)", Platform = "C519", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            
            // Lincoln Cars
            new VehiclePlatform { DisplayName = "Lincoln MKZ (2013-2020)", Platform = "CD4", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln Continental (2017-2020)", Platform = "D544", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln MKS (2009-2016)", Platform = "CD391", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
            new VehiclePlatform { DisplayName = "Lincoln MKT (2010-2019)", Platform = "D471", Brand = "Lincoln", SupportsKeyless = true, MsCanBaud = 125000, HsCanBaud = 500000 },
        };

        /// <summary>
        /// Gets all supported vehicles sorted by display name
        /// </summary>
        public static IEnumerable<VehiclePlatform> GetAllVehicles()
        {
            var sorted = new List<VehiclePlatform>(_vehicles);
            sorted.Sort((a, b) => a.DisplayName.CompareTo(b.DisplayName));
            return sorted;
        }

        /// <summary>
        /// Gets vehicles by brand
        /// </summary>
        public static IEnumerable<VehiclePlatform> GetVehiclesByBrand(string brand)
        {
            foreach (var v in _vehicles)
            {
                if (v.Brand == brand)
                    yield return v;
            }
        }

        /// <summary>
        /// Finds a vehicle by display name
        /// </summary>
        public static VehiclePlatform? FindByName(string displayName)
        {
            foreach (var v in _vehicles)
            {
                if (v.DisplayName == displayName)
                    return v;
            }
            return null;
        }

        /// <summary>
        /// Finds vehicles by platform code
        /// </summary>
        public static IEnumerable<VehiclePlatform> FindByPlatform(string platform)
        {
            foreach (var v in _vehicles)
            {
                if (v.Platform == platform)
                    yield return v;
            }
        }
    }

    /// <summary>
    /// Vehicle platform definition
    /// </summary>
    public class VehiclePlatform
    {
        public string DisplayName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Brand { get; set; } = "";
        public bool SupportsKeyless { get; set; }
        public uint HsCanBaud { get; set; } = 500000;
        public uint MsCanBaud { get; set; } = 125000;
    }
}
