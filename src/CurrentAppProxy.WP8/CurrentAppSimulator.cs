﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Windows.ApplicationModel.Store;
using Windows.Storage;

namespace MarkedUp
{
#if DEBUG //This class should not exist in production, just like the real thing on WinRT
    public class CurrentAppSimulator
    {
        private static readonly DateTime DEVELOPER_LICENSE_EXPIRES = new DateTime(504911232000000000, DateTimeKind.Utc);

        private static CurrentAppSimulator _instance = DefaultAppSimulator();

        private readonly IDictionary<string, bool> _methodResults;
        private readonly AppListing _listingInformation;
        private readonly LicenseInformation _licenseInformation;

        private CurrentAppSimulator(AppListing listingInformation, LicenseInformation licenseInformation,
                                    IDictionary<string, bool> methodResults)
        {
            _listingInformation = listingInformation;
            _licenseInformation = licenseInformation;
            _methodResults = methodResults;
        }

        #region public methods


        public static async Task ReloadSimulatorAsync(StorageFile storageFile)
        {
            using (var stream = await storageFile.OpenStreamForReadAsync().ConfigureAwait(false))
            {
                var document = XDocument.Load(stream);
                _instance = FromXml(document);
            }
        }

        public static Guid AppId
        {
            get { return _instance._listingInformation.AppId; }
        }

        public static Uri LinkUri
        {
            get { return _instance._listingInformation.LinkUri; }
        }

        public static string CurrentMarket
        {
            get { return _instance._listingInformation.ListingInformation.CurrentMarket; }
        }

        public static LicenseInformation LicenseInformation
        {
            get { return _instance._licenseInformation; }
        }

        public static async Task<ListingInformation> LoadListingInformationAsync()
        {
            return await Task.Run(() =>
                {
                    if (!_instance._methodResults["LoadListingInformationAsync_GetResult"])
                        throw new ApplicationException("LoadListingInformationAsync was programmed to fail in CurrentAppSimulator settings");
                    return _instance._listingInformation.ListingInformation;
                });
        }

        public static async Task<string> RequestProductPurchaseAsync(string productId, bool includeReceipt)
        {
            return await Task.Run(() =>
            {
                if (!_instance._methodResults["RequestProductPurchaseAsync_GetResult"])
                    throw new ApplicationException("RequestProductPurchaseAsync was programmed to fail in CurrentAppSimulator settings");

                //This product exists, and a user doesn't already have a license for it if it's not consumable
                if (_instance._listingInformation.ListingInformation.ProductListings.ContainsKey(productId) 
                    && !(_instance._licenseInformation.ProductLicenses.ContainsKey(productId)
                     && _instance._listingInformation.ListingInformation.ProductListings[productId].ProductType != ProductType.Consumable
                    ))
                {
                    var productListing = _instance._listingInformation.ListingInformation.ProductListings[productId];
                    var xml = CreateProductReceipt(productListing);

                    _instance._licenseInformation.ProductLicenses.Add(productListing.ProductId, 
                        new ProductLicense(){ ExpirationDate = DEVELOPER_LICENSE_EXPIRES, 
                            IsActive = true, IsConsumable = productListing.ProductType == ProductType.Consumable, 
                            ProductId = productListing.ProductId});
                    return xml;
                }
                else if (!(_instance._licenseInformation.ProductLicenses.ContainsKey(productId)
                     && _instance._listingInformation.ListingInformation.ProductListings[productId].ProductType != ProductType.Consumable))
                {
                    throw new ArgumentException(string.Format("User already has a license for {0}", productId));
                }
                else
                {
                    throw new ArgumentException(string.Format("In-app purchase {0} not found in CurrentAppSimulator listing information", productId));
                }
            });
        }

        public static async Task<string> RequestAppPurchaseAsync(bool includeReceipt)
        {
            return await Task.Run(() =>
            {
                if (!_instance._methodResults["RequestAppPurchaseAsync_GetResult"])
                    throw new ApplicationException("RequestAppPurchaseAsync was programmed to fail in CurrentAppSimulator settings");

                if (_instance._licenseInformation.IsActive)
                    throw new ApplicationException("User already owns this application");

                return CreateAppPurchaseReceipt(_instance._listingInformation);
            });
        }

        public static async Task<string> GetAppReceiptAsync()
        {
            return await Task.Run(() =>
            {
                if (!_instance._methodResults["GetAppReceiptAsync_GetResult"])
                    throw new ApplicationException("GetAppReceiptAsync was programmed to fail in CurrentAppSimulator settings");

                return "purchased " + AppId; //TODO: replace with appropriate XML
            });
        }

        public static async Task<string> GetProductReceiptAsync(string productId)
        {
            return await Task.Run(() =>
            {
                if (!_instance._methodResults["GetProductReceiptAsync_GetResult"])
                    throw new ApplicationException("GetProductReceiptAsync was programmed to fail in CurrentAppSimulator settings");

                //This product exists AND a user has bought it
                if (_instance._listingInformation.ListingInformation.ProductListings.ContainsKey(productId) && _instance._licenseInformation.ProductLicenses.ContainsKey(productId))
                {
                    var productListing = _instance._listingInformation.ListingInformation.ProductListings[productId];
                    var xml = CreateProductReceipt(productListing);
                    return xml;
                }
                else if (_instance._licenseInformation.ProductLicenses.ContainsKey(productId))
                {
                    //no receipt
                    return null;
                }
                else
                {
                    throw new ApplicationException(string.Format("In-app purchase {0} not found in CurrentAppSimulator listing information", productId));
                }
            });
        }

        #endregion

        #region Default CurrentAppSimulator settings

        /// <summary>
        /// Populates a default CurrentAppSimulator implementation based off of what's available in the WMAppManifest.xml file
        /// </summary>
        /// <returns>A CurrentAppSimulator instance populated via the contents of the WMAppManifest.xml file</returns>
        private static CurrentAppSimulator DefaultAppSimulator()
        {
            var appProperties = new AppListing();
            appProperties.ListingInformation = new ListingInformation();
            var appManifestXml = XDocument.Load(
               "WMAppManifest.xml");

            using (var rdr = appManifestXml.CreateReader(ReaderOptions.None))
            {
                rdr.ReadToDescendant("App");
                if (!rdr.IsStartElement())
                {
                    throw new System.FormatException("WMAppManifest.xml is missing.");
                }

                var productId = rdr.GetAttribute("ProductID");
                appProperties.AppId = Guid.Parse(productId);
                appProperties.LinkUri = new Uri(string.Format("https://store.windows.com/en-US/{0}", appProperties.AppId.ToString()));
                appProperties.ListingInformation.CurrentMarket = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                appProperties.ListingInformation.Description = rdr.GetAttribute("Description");
                appProperties.ListingInformation.Name = rdr.GetAttribute("Title");
                appProperties.ListingInformation.FormattedPrice = string.Format("{0}{1}",
                                                                                RegionInfo.CurrentRegion
                                                                                          .ISOCurrencySymbol, 0.00d);
            }

            return new CurrentAppSimulator(appProperties, DefaultLicenseInformation(), DefaultMethodResults());
        }

        #endregion

        #region XML populating methods (used for populating the CurrentAppSimulator object)

        private static CurrentAppSimulator FromXml(XDocument simulatorXml)
        {
            var listingInformation = GetListingFromXml(simulatorXml.Descendants("ListingInformation"));
            var licenseInformation = GetLicenseInformationFromXml(simulatorXml.Descendants("LicenseInformation"));
            var methodSimulations = GetMethodSimulationsFromXml(simulatorXml.Descendants("Simulation"));

            return new CurrentAppSimulator(listingInformation, licenseInformation, methodSimulations);
        }

        private static IDictionary<string, bool> GetMethodSimulationsFromXml(IEnumerable<XElement> simulationNodes)
        {
            var simulatedResponses = DefaultMethodResults();

            foreach (var defaultResponseNode in simulationNodes.Elements("DefaultResponse"))
            {
                var methodName = defaultResponseNode.Attribute("MethodName").SafeRead();
                var hResult = defaultResponseNode.Attribute("HResult").SafeRead("E_FAIL");
                var methodSucceeds = !hResult.Equals("E_FAIL");

                if (simulatedResponses.ContainsKey(methodName))
                    simulatedResponses[methodName] = methodSucceeds;
                else
                {
                    simulatedResponses.Add(methodName, methodSucceeds);
                }

            }

            return simulatedResponses;
        }

        private static LicenseInformation GetLicenseInformationFromXml(IEnumerable<XElement> licenseNodes)
        {
            var li = new LicenseInformation();

            var licenseNode = licenseNodes.First();

            var expirationString = licenseNode.Element("ExpirationDate").SafeRead();
            li.ExpirationDate = CalculateExpirationDate(expirationString);
            li.IsActive = bool.Parse(licenseNode.Element("IsActive").SafeRead("true"));
            li.IsTrial = bool.Parse(licenseNode.Element("IsTrial").SafeRead("true"));

            //Parse in-app-purchase-specific licenses
            foreach (var productLicenseNode in licenseNode.Elements("Product"))
            {
                var productId = productLicenseNode.Attribute("ProductId").SafeRead();
                var isActive = bool.Parse(productLicenseNode.Element("IsActive").SafeRead("false"));
                var isConsumable = bool.Parse(productLicenseNode.Element("IsConsumable").SafeRead("false"));
                var expirationDate = CalculateExpirationDate(productLicenseNode.Element("ExpirationDate").SafeRead());
                li.ProductLicenses.Add(productId, new ProductLicense() { ProductId = productId, ExpirationDate = expirationDate, IsActive = isActive, IsConsumable = isConsumable });
            }

            return li;
        }

        /// <summary>
        /// Calculates a license expiration date based off what's included in the WindowsStoreProxy.xml file
        /// </summary>
        /// <param name="expirationString"></param>
        /// <returns></returns>
        private static DateTime CalculateExpirationDate(string expirationString)
        {
            DateTime expirationDate;
            if (String.IsNullOrEmpty(expirationString) || !DateTime.TryParse(expirationString, out expirationDate))
                expirationDate = DEVELOPER_LICENSE_EXPIRES;
            //The date a developer license expires ({12/31/1600 12:00:00 AM UTC})
            return expirationDate;
        }

        private static AppListing GetListingFromXml(IEnumerable<XElement> listingNodes)
        {
            var resultantListing = new AppListing();
            var listingInfo = new ListingInformation();
            var node = listingNodes.First();
            {
                var appNode = node.Element("App");
                resultantListing.AppId = Guid.Parse((string)appNode.Element("AppId"));
                resultantListing.LinkUri = new Uri((string)appNode.Element("LinkUri"));
                listingInfo.CurrentMarket = appNode.Element("CurrentMarket").SafeRead();

                //Parse out the correct ISO country code for the current market based on the region information included in the XML file
                if (listingInfo.CurrentMarket == null)
                    listingInfo.CurrentMarket = RegionInfo.CurrentRegion.TwoLetterISORegionName; //use current region by default
                else
                    listingInfo.CurrentMarket = new RegionInfo(listingInfo.CurrentMarket).TwoLetterISORegionName;

                var marketData = appNode.Element("MarketData");

                listingInfo.Description = marketData.Element("Description").SafeRead();
                listingInfo.Name = (string)marketData.Element("Name");
                listingInfo.FormattedPrice = string.Format("{0}{1}", (string)marketData.Element("CurrencySymbol"),
                                             Double.Parse(marketData.Element("Price").SafeRead("0.00")).ToString("0.00", CultureInfo.CurrentUICulture));

                //Products
                foreach (var product in node.Elements("Product"))
                {
                    var productListing = new ProductListing();
                    productListing.ProductId = product.Attribute("ProductId").SafeRead();

                    var iapMarketData = product.Element("MarketData");

                    productListing.Description = iapMarketData.Element("Description").SafeRead();
                    productListing.Name = iapMarketData.Element("Name").SafeRead();
                    productListing.FormattedPrice = string.Format("{0}{1}", (string)iapMarketData.Element("CurrencySymbol"),
                                             Double.Parse(iapMarketData.Element("Price").SafeRead("0.00")).ToString("0.00", CultureInfo.CurrentUICulture));
                    productListing.ProductType = (ProductType)iapMarketData.Element("ProductType").SafeRead(ProductType.Unknown);
                    listingInfo.ProductListings.Add(productListing.ProductId, productListing);

                }
            }

            resultantListing.ListingInformation = listingInfo;
            return resultantListing;
        }



        /// <summary>
        /// Loads all of the defaults for simulating CurrentAppSimulator failures on WP8
        /// </summary>
        /// <returns>A populated read-only dictionary containing method names and a boolean indicating if the value is successful or not</returns>
        private static IDictionary<string, bool> DefaultMethodResults()
        {
            return new Dictionary<string, bool>() { 
                { "LoadListingInformationAsync_GetResult", true }, 
                { "RequestProductPurchaseAsync_GetResult", true },
                { "RequestAppPurchaseAsync_GetResult", true },
                { "GetAppReceiptAsync_GetResult", true },
                { "GetProductReceiptAsync_GetResult", true }
            };
        }

        /// <summary>
        /// Default license information if none is specified through Simulator settings
        /// </summary>
        /// <returns>A populated LicenseInformation object</returns>
        private static LicenseInformation DefaultLicenseInformation()
        {
            return new LicenseInformation()
                {
                    ExpirationDate = DEVELOPER_LICENSE_EXPIRES,
                    IsActive = true,
                    IsTrial = true
                };
        }

        #endregion

        #region Reciept factory for completed purchases

        public const string ProductReceiptXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?> 
<Receipt Version=""1.0"" ReceiptDate=""{0}"" CertificateId=""{1}"" ReceiptDeviceId=""{2}"">
  <ProductReceipt Id=""{3}"" AppId=""{4}"" ProductId=""{5}"" PurchaseDate=""{0}"" ProductType=""{6}"" /> 
 </Receipt>";
        internal static string CreateProductReceipt(ProductListing purchasedProduct)
        {
            var purchaseDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var certificateId = String.Empty;
            var productId = purchasedProduct.ProductId;
            var productType = purchasedProduct.ProductType;
            var deviceId = Guid.NewGuid();
            var recieptId = Guid.NewGuid();

            return string.Format(ProductReceiptXml, purchaseDate, certificateId, deviceId, recieptId, AppId, productId,
                productType);
        }

        public const string AppReceiptXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?> 
<Receipt Version=""1.0"" ReceiptDate=""{0}"" CertificateId=""{1}"" ReceiptDeviceId=""{2}"">
  <AppReceipt Id=""{3}"" AppId=""{4}"" PurchaseDate=""{0}"" LicenseType=""{5}"" /> 
 </Receipt>";
        internal static string CreateAppPurchaseReceipt(AppListing listing)
        {
            var purchaseDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var certificateId = String.Empty;
            var licenseType = "Full";
            var deviceId = Guid.NewGuid();
            var recieptId = Guid.NewGuid();

            return string.Format(AppReceiptXml, purchaseDate, certificateId, deviceId, recieptId, AppId, licenseType);
        }

        #endregion

    }

    #region AppListing class - used to hold internal state for WP8 - CurrentAppSimulator

    class AppListing
    {
        public Guid AppId { get; set; }
        public Uri LinkUri { get; set; }
        public ListingInformation ListingInformation { get; set; }
    }

    #endregion

    #region XDocument (Linq-to-XML) extension methods to make parsing fun and safe!

    public static class XDocumentExtensions
    {
        public static string SafeRead(this XElement element, string defaultValue = null)
        {
            return element == null ? defaultValue : element.Value;
        }

        public static string SafeRead(this XAttribute attribute, string defaultValue = null)
        {
            return attribute == null ? defaultValue : attribute.Value;
        }

        public static Enum SafeRead(this XElement element, Enum defaultValueIfNull)
        {
            if (element == null)
                return defaultValueIfNull;

            return (Enum)Enum.Parse(defaultValueIfNull.GetType(), element.Value);
        }
    }

    #endregion



#endif

}
