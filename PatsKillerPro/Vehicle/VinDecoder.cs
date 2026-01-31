using System;
using System.Collections.Generic;
using PatsKillerPro.Utils;

namespace PatsKillerPro.Vehicle
{
    /// <summary>
    /// Decodes VIN to identify Ford/Lincoln vehicles
    /// </summary>
    public static class VinDecoder
    {
        // VIN Position Reference:
        // 1-3: WMI (World Manufacturer Identifier)
        // 4-8: VDS (Vehicle Descriptor Section)
        // 9: Check digit
        // 10: Model year
        // 11: Plant code
        // 12-17: Serial number

        // Ford WMIs
        private static readonly HashSet<string> FordWmis = new()
        {
            "1FA", "1FB", "1FC", "1FD", "1FM", "1FT", "1FU", "1FV",  // USA Ford
            "2FA", "2FB", "2FM", "2FT",                              // Canada Ford
            "3FA", "3FM", "3FT",                                      // Mexico Ford
            "WF0", "WF1",                                             // Germany Ford (Europe)
            "SFA",                                                     // UK Ford
            "6FP", "6G1",                                             // Australia Ford
            "NM0",                                                     // Turkey Ford
            "VS6",                                                     // Spain Ford
            "XLC",                                                     // Netherlands Ford
            "MAJ",                                                     // India Ford
            "LVS",                                                     // China Ford
        };

        // Lincoln WMIs
        private static readonly HashSet<string> LincolnWmis = new()
        {
            "1LN", "2LM", "3LN", "5LM"
        };

        // Model year codes (position 10)
        private static readonly Dictionary<char, int> YearCodes = new()
        {
            {'A', 2010}, {'B', 2011}, {'C', 2012}, {'D', 2013}, {'E', 2014},
            {'F', 2015}, {'G', 2016}, {'H', 2017}, {'J', 2018}, {'K', 2019},
            {'L', 2020}, {'M', 2021}, {'N', 2022}, {'P', 2023}, {'R', 2024},
            {'S', 2025}, {'T', 2026}, {'V', 2027}, {'W', 2028}, {'X', 2029},
            {'Y', 2030}, {'1', 2031}, {'2', 2032}, {'3', 2033}, {'4', 2034},
            {'5', 2035}, {'6', 2036}, {'7', 2037}, {'8', 2038}, {'9', 2039}
        };

        // VDS patterns to vehicle mapping
        // Format: positions 4-8 (0-indexed 3-7) -> vehicle info
        private static readonly Dictionary<string, (string Platform, string Model, bool Keyless)> VdsMapping = new()
        {
            // Ford F-150 (P702 - 2021+)
            {"W1E5P", ("P702", "Ford F-150 XL", false)},
            {"W1E1P", ("P702", "Ford F-150 XLT", false)},
            {"W1C5P", ("P702", "Ford F-150 Lariat", false)},
            {"W1C1P", ("P702", "Ford F-150 King Ranch", true)},
            {"W1R5P", ("P702", "Ford F-150 Raptor", true)},
            
            // Ford F-150 (P552 - 2015-2020)
            {"X1E5C", ("P552", "Ford F-150", false)},
            {"X1C5C", ("P552", "Ford F-150", false)},
            
            // Ford Fusion / Mondeo (CD4)
            {"P0CE6", ("CD4", "Ford Fusion S", true)},
            {"P0CG6", ("CD4", "Ford Fusion SE", true)},
            {"P0CH6", ("CD4", "Ford Fusion Titanium", true)},
            {"P0HE6", ("CD4", "Ford Fusion Hybrid", true)},
            {"P0JE6", ("CD4", "Ford Fusion Energi", true)},
            {"WPCGW", ("CD4", "Ford Mondeo", true)},
            
            // Ford Focus (C346 - Gen 3)
            {"P3CY5", ("C346", "Ford Focus S", true)},
            {"P3CY6", ("C346", "Ford Focus SE", true)},
            {"P3CW5", ("C346", "Ford Focus Titanium", true)},
            {"P3RS5", ("C346", "Ford Focus RS", true)},
            {"P3ST5", ("C346", "Ford Focus ST", true)},
            
            // Ford Focus (C519 - Gen 4)
            {"P8CF5", ("C519", "Ford Focus", true)},
            {"P8CW5", ("C519", "Ford Focus Active", true)},
            
            // Ford Explorer (U625 - 2020+)
            {"K8AG6", ("U625", "Ford Explorer", true)},
            {"K8AW6", ("U625", "Ford Explorer Limited", true)},
            {"K8AS6", ("U625", "Ford Explorer ST", true)},
            {"K8AP6", ("U625", "Ford Explorer Platinum", true)},
            
            // Ford Edge (CD539)
            {"K4BB5", ("CD539", "Ford Edge SE", true)},
            {"K4BG5", ("CD539", "Ford Edge SEL", true)},
            {"K4BW5", ("CD539", "Ford Edge Titanium", true)},
            {"K4BS5", ("CD539", "Ford Edge ST", true)},
            
            // Ford Escape (CX482 - 2020+)
            {"U0GD5", ("CX482", "Ford Escape S", true)},
            {"U0GG5", ("CX482", "Ford Escape SE", true)},
            {"U0GJ5", ("CX482", "Ford Escape SEL", true)},
            {"U0GW5", ("CX482", "Ford Escape Titanium", true)},
            
            // Ford Transit (V363)
            {"E2XY2", ("V363", "Ford Transit", false)},
            {"E2XX2", ("V363", "Ford Transit 150", false)},
            {"E2XZ2", ("V363", "Ford Transit 250", false)},
            {"E2YY2", ("V363", "Ford Transit 350", false)},
            
            // Ford Transit Connect (V408)
            {"E6XW5", ("V408", "Ford Transit Connect", false)},
            
            // Ford Ranger (P703 - 2022+)
            {"R1E55", ("P703", "Ford Ranger XL", false)},
            {"R1G55", ("P703", "Ford Ranger XLT", false)},
            {"R1W55", ("P703", "Ford Ranger Lariat", true)},
            
            // Ford Ranger (P375 - 2019-2022)
            {"R1CC5", ("P375", "Ford Ranger", false)},
            {"R1CK5", ("P375", "Ford Ranger XLT", false)},
            
            // Ford Bronco (U725)
            {"N5EE5", ("U725", "Ford Bronco", true)},
            {"N5EG5", ("U725", "Ford Bronco Big Bend", true)},
            {"N5EW5", ("U725", "Ford Bronco Badlands", true)},
            {"N5ES5", ("U725", "Ford Bronco Raptor", true)},
            
            // Ford Bronco Sport (CX430)
            {"N5AD5", ("CX430", "Ford Bronco Sport", true)},
            {"N5AG5", ("CX430", "Ford Bronco Sport Big Bend", true)},
            {"N5AW5", ("CX430", "Ford Bronco Sport Badlands", true)},
            
            // Ford Maverick (P758)
            {"M8AD5", ("P758", "Ford Maverick XL", true)},
            {"M8AG5", ("P758", "Ford Maverick XLT", true)},
            {"M8AW5", ("P758", "Ford Maverick Lariat", true)},
            
            // Ford Fiesta (B479 - Gen 7)
            {"P4GD5", ("B479", "Ford Fiesta S", true)},
            {"P4GG5", ("B479", "Ford Fiesta SE", true)},
            {"P4GW5", ("B479", "Ford Fiesta Titanium", true)},
            {"P4ST5", ("B479", "Ford Fiesta ST", true)},
            
            // Ford Fiesta (B299 - Gen 6)
            {"P4CD5", ("B299", "Ford Fiesta", true)},
            
            // Ford EcoSport (B2E)
            {"J1DD5", ("B2E", "Ford EcoSport S", false)},
            {"J1DG5", ("B2E", "Ford EcoSport SE", false)},
            {"J1DW5", ("B2E", "Ford EcoSport Titanium", true)},
            
            // Ford Expedition (P702 - 2018+)
            {"J8EP6", ("P702", "Ford Expedition", true)},
            {"J8EW6", ("P702", "Ford Expedition Limited", true)},
            {"J8ES6", ("P702", "Ford Expedition King Ranch", true)},
            {"J8EP7", ("P702", "Ford Expedition MAX", true)},
            
            // Lincoln Navigator (P702)
            {"J8LP6", ("P702", "Lincoln Navigator", true)},
            {"J8LW6", ("P702", "Lincoln Navigator Reserve", true)},
            {"J8LB6", ("P702", "Lincoln Navigator Black Label", true)},
            
            // Lincoln Aviator (U611)
            {"A8AG6", ("U611", "Lincoln Aviator", true)},
            {"A8AW6", ("U611", "Lincoln Aviator Reserve", true)},
            {"A8AB6", ("U611", "Lincoln Aviator Black Label", true)},
            
            // Lincoln Nautilus (CD539)
            {"K4LG5", ("CD539", "Lincoln Nautilus", true)},
            {"K4LW5", ("CD539", "Lincoln Nautilus Reserve", true)},
            {"K4LB5", ("CD539", "Lincoln Nautilus Black Label", true)},
            
            // Lincoln Corsair (CX483)
            {"U0LG5", ("CX483", "Lincoln Corsair", true)},
            {"U0LW5", ("CX483", "Lincoln Corsair Reserve", true)},
            {"U0LB5", ("CX483", "Lincoln Corsair Black Label", true)},
            
            // Lincoln MKZ (CD4)
            {"P0LG6", ("CD4", "Lincoln MKZ", true)},
            {"P0LW6", ("CD4", "Lincoln MKZ Reserve", true)},
            {"P0LB6", ("CD4", "Lincoln MKZ Black Label", true)},
            {"P0LH6", ("CD4", "Lincoln MKZ Hybrid", true)},
            
            // Lincoln Continental (D544)
            {"A0LG6", ("D544", "Lincoln Continental", true)},
            {"A0LW6", ("D544", "Lincoln Continental Reserve", true)},
            {"A0LB6", ("D544", "Lincoln Continental Black Label", true)},
        };

        /// <summary>
        /// Decodes a VIN to vehicle information
        /// </summary>
        public static VehicleInfo? Decode(string vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length < 17)
            {
                Logger.Warning($"Invalid VIN length: {vin?.Length ?? 0}");
                return null;
            }

            vin = vin.ToUpperInvariant().Trim();

            // Get WMI (first 3 characters)
            var wmi = vin.Substring(0, 3);
            
            // Check if Ford or Lincoln
            bool isFord = FordWmis.Contains(wmi);
            bool isLincoln = LincolnWmis.Contains(wmi);
            
            if (!isFord && !isLincoln)
            {
                Logger.Warning($"VIN not Ford/Lincoln: WMI={wmi}");
                return null;
            }

            // Get model year (position 10, index 9)
            var yearCode = vin[9];
            int modelYear = 0;
            if (YearCodes.TryGetValue(yearCode, out int year))
            {
                modelYear = year;
            }

            // Get VDS (positions 4-8, index 3-7)
            var vds = vin.Substring(3, 5);

            // Try exact VDS match first
            if (VdsMapping.TryGetValue(vds, out var exactMatch))
            {
                return CreateVehicleInfo(vin, exactMatch.Platform, exactMatch.Model, exactMatch.Keyless, modelYear);
            }

            // Try partial VDS match (first 3 characters)
            var vdsPartial = vds.Substring(0, 3);
            foreach (var kvp in VdsMapping)
            {
                if (kvp.Key.StartsWith(vdsPartial))
                {
                    return CreateVehicleInfo(vin, kvp.Value.Platform, kvp.Value.Model, kvp.Value.Keyless, modelYear);
                }
            }

            // Try to identify by VDS pattern even if not in mapping
            var guessedInfo = GuessVehicleFromVds(vds, isLincoln, modelYear);
            if (guessedInfo != null)
            {
                return CreateVehicleInfo(vin, guessedInfo.Value.Platform, guessedInfo.Value.Model, 
                    guessedInfo.Value.Keyless, modelYear);
            }

            Logger.Warning($"Could not decode VIN: {vin}, VDS={vds}");
            return null;
        }

        private static VehicleInfo CreateVehicleInfo(string vin, string platform, string model, bool keyless, int year)
        {
            var displayName = year > 0 ? $"{model} ({year})" : model;
            
            Logger.Info($"VIN decoded: {vin} -> {displayName}, Platform: {platform}, Keyless: {keyless}");
            
            return new VehicleInfo
            {
                VIN = vin,
                Platform = platform,
                Model = model,
                ModelYear = year,
                DisplayName = displayName,
                SupportsKeyless = keyless
            };
        }

        private static (string Platform, string Model, bool Keyless)? GuessVehicleFromVds(string vds, bool isLincoln, int year)
        {
            // Common VDS patterns for guessing
            var firstChar = vds[0];
            
            if (isLincoln)
            {
                return firstChar switch
                {
                    'A' when vds[1] == '8' => ("U611", "Lincoln Aviator", true),
                    'A' when vds[1] == '0' => ("D544", "Lincoln Continental", true),
                    'J' when vds[1] == '8' => ("P702", "Lincoln Navigator", true),
                    'K' when vds[1] == '4' => ("CD539", "Lincoln Nautilus", true),
                    'U' when vds[1] == '0' => ("CX483", "Lincoln Corsair", true),
                    'P' when vds[1] == '0' => ("CD4", "Lincoln MKZ", true),
                    _ => null
                };
            }
            else // Ford
            {
                return firstChar switch
                {
                    'W' or 'X' => ("P552", "Ford F-150", false),
                    'P' when vds[1] == '0' => ("CD4", "Ford Fusion", true),
                    'P' when vds[1] == '3' => ("C346", "Ford Focus", true),
                    'P' when vds[1] == '8' => ("C519", "Ford Focus", true),
                    'P' when vds[1] == '4' => ("B479", "Ford Fiesta", true),
                    'K' when vds[1] == '8' => ("U625", "Ford Explorer", true),
                    'K' when vds[1] == '4' => ("CD539", "Ford Edge", true),
                    'U' when vds[1] == '0' => ("CX482", "Ford Escape", true),
                    'E' when vds[1] == '2' => ("V363", "Ford Transit", false),
                    'E' when vds[1] == '6' => ("V408", "Ford Transit Connect", false),
                    'R' when vds[1] == '1' => year >= 2022 ? ("P703", "Ford Ranger", false) : ("P375", "Ford Ranger", false),
                    'N' when vds[1] == '5' && vds[2] == 'E' => ("U725", "Ford Bronco", true),
                    'N' when vds[1] == '5' && vds[2] == 'A' => ("CX430", "Ford Bronco Sport", true),
                    'M' when vds[1] == '8' => ("P758", "Ford Maverick", true),
                    'J' when vds[1] == '1' => ("B2E", "Ford EcoSport", false),
                    'J' when vds[1] == '8' => ("P702", "Ford Expedition", true),
                    _ => null
                };
            }
        }

        /// <summary>
        /// Validates VIN checksum (position 9)
        /// </summary>
        public static bool ValidateChecksum(string vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length != 17)
                return false;

            var transliteration = new Dictionary<char, int>
            {
                {'A', 1}, {'B', 2}, {'C', 3}, {'D', 4}, {'E', 5}, {'F', 6}, {'G', 7}, {'H', 8},
                {'J', 1}, {'K', 2}, {'L', 3}, {'M', 4}, {'N', 5}, {'P', 7}, {'R', 9},
                {'S', 2}, {'T', 3}, {'U', 4}, {'V', 5}, {'W', 6}, {'X', 7}, {'Y', 8}, {'Z', 9},
                {'0', 0}, {'1', 1}, {'2', 2}, {'3', 3}, {'4', 4}, {'5', 5}, {'6', 6}, {'7', 7}, {'8', 8}, {'9', 9}
            };

            var weights = new[] { 8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2 };
            
            int sum = 0;
            for (int i = 0; i < 17; i++)
            {
                if (i == 8) continue; // Skip check digit position
                if (!transliteration.TryGetValue(vin[i], out int value))
                    return false;
                sum += value * weights[i];
            }

            int remainder = sum % 11;
            char expectedCheck = remainder == 10 ? 'X' : (char)('0' + remainder);

            return vin[8] == expectedCheck;
        }
    }

    /// <summary>
    /// Vehicle information decoded from VIN
    /// </summary>
    public class VehicleInfo
    {
        public string VIN { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Model { get; set; } = "";
        public int ModelYear { get; set; }
        public string DisplayName { get; set; } = "";
        public bool SupportsKeyless { get; set; }
        
        // Additional properties for compatibility
        public string Vin { get => VIN; set => VIN = value; }
        public bool Keyless { get => SupportsKeyless; set => SupportsKeyless = value; }
        public bool RequiresGatewayUnlock { get; set; }
        public CanConfiguration CanConfig { get; set; } = CanConfiguration.SingleHsCan;
        
        // Computed properties
        public int Year => ModelYear;
        public bool Is2020Plus => ModelYear >= 2020;
    }

    /// <summary>
    /// CAN bus configuration type
    /// </summary>
    public enum CanConfiguration
    {
        SingleHsCan,
        DualCan,
        HsCanMsCan
    }
}
