﻿using System;
using System.Configuration;
using System.Web;
using System.Web.Mvc;
using AppPoolManager;
using BLL.Dto;
using BLL.Enums;
using BLL.Interfaces;
using BLL.Services;
using Microsoft.AspNet.Identity.Owin;
using WebUI.FullFramework.Core;
using WebUI.FullFramework.Enums;
using WebUI.FullFramework.Models;

namespace WebUI.FullFramework.Controllers
{
    public class IISController : Controller
    {
        private IFeatureService FeatureService => HttpContext.GetOwinContext().GetUserManager<IFeatureService>();
        private IConnectionStringsService ConnectionStringsService => HttpContext.GetOwinContext().GetUserManager<IConnectionStringsService>();
        private IApplicationPoolService ApplicationPoolService => HttpContext.GetOwinContext().GetUserManager<IApplicationPoolService>();
        private IRedisService RedisService => HttpContext.GetOwinContext().GetUserManager<IRedisService>();

        // TODO Change for view model
        [Authorize]
        public ActionResult Index(
            string applicationPath = null,
            string siteName = null,
            IISSiteType siteType = IISSiteType.Default, 
            IISViewActionType viewActionType = IISViewActionType.InformationComponent)
        {
            string db = null;
            string redisDb = null;
            if (!string.IsNullOrEmpty(applicationPath))
            {
                switch (siteType)
                {
                    case IISSiteType.Application:
                        db = ConnectionStringsService.GetMssqlDb(applicationPath, false);
                        redisDb = ConnectionStringsService.GetRedisDb(applicationPath, false);
                        break;
                    case IISSiteType.Site:
                        db = ConnectionStringsService.GetMssqlDb(applicationPath, true);
                        redisDb = ConnectionStringsService.GetRedisDb(applicationPath, true);
                        break;
                    default:
                        var exception = new Exception("Такой тип приложения не поддерживается");
                        NLogger.Log(exception);
                        throw exception;
                }
                
            }

            var viewModel = new IISIndexViewModel
            {
                SiteName = siteName,
                ApplicationPath = applicationPath,
                SiteType = siteType,
                ActionViewType = viewActionType,
                Database = db,
                RedisDatabase = redisDb,
                IsFeatureTableExist = !string.IsNullOrEmpty(db) &&
                                      FeatureService.GetFeatureTableExist(db).GetAwaiter().GetResult()
            };

            if (!string.IsNullOrEmpty(applicationPath))
            {
                SiteInformation information;
                // TODO Extract interface for DI
                using (var infoService = new SiteInformationService())
                {
                    information = infoService.GetInformationBySiteType(applicationPath, siteType);
                }

                viewModel.ApplicationPoolName = information.ApplicationPoolName;
                viewModel.IsPoolStoppingOrStopped =
                    ApplicationPoolService.IsPoolStoppingOrStopped(information.ApplicationPoolName);
                viewModel.IsPoolStartingOrStarted =
                    ApplicationPoolService.IsPoolStartingOrStarted(information.ApplicationPoolName);
            }
            
            return View(viewModel);
        }

        #region Methods: Partial Views (Components)

        [Authorize]
        [ChildActionOnly]
        public PartialViewResult GetFeaturesComponent(string db, int redisDb)
        {
            ViewBag.RedisDb = redisDb;
            ViewBag.Database = db;
            var features = FeatureService.GetFeatures(db).GetAwaiter().GetResult();
            return PartialView("_FeaturesComponent", features);
        }

        [Authorize]
        [ChildActionOnly]
        public PartialViewResult GetConnectionStringsComponent(string path, IISSiteType siteType)
        {
            //var data = ConnectionStringsService.GetSiteConnectionStrings(path, siteType == IISSiteType.Site);
            //var connectionStrings = data.ConnectionStrings;
            return PartialView("_ConnectionStringsComponent");
        }

        [Authorize(Roles = "Admin, Developers")]
        [ChildActionOnly]
        public PartialViewResult GetConfigurationFileComponent(string path, IISSiteType siteType)
        {
            var filePath = ConnectionStringsService.GetConfigurationFilePath(path, siteType);

            var fileReader = new FileReader();
            var fileText = fileReader.ReadTextFile(filePath);
            return PartialView("_ConfigurationFileComponent", fileText);
        }

        [Authorize]
        [ChildActionOnly]
        public PartialViewResult GetInformationComponent(string path, IISSiteType siteType)
        {
            SiteInformation information;
            // TODO Extract interface for DI
            using (var infoService = new SiteInformationService())
            {
                information = infoService.GetInformationBySiteType(path, siteType);
            }
            return PartialView("_InformationComponent", information);
        }

        [Authorize]
        public PartialViewResult GetDeleteApplicationModal(string pathOrName, string siteName, IISSiteType siteType, string database)
        {
            ViewBag.PathOrName = pathOrName;
            ViewBag.SiteName = siteName;
            ViewBag.SiteType = siteType;
            ViewBag.Database = database;
            return PartialView("_DeleteApplicationModal");
        }

        #endregion

        #region Methods: API 

        [HttpPost]
        [Authorize]
        [ActionLogger]
        public JsonResult SaveFeatures(FeaturesComponentUpdateModel featuresUpdateModel)
        {
            try
            {
                FeatureService.UpdateFeatures(featuresUpdateModel.Features, featuresUpdateModel.Db, featuresUpdateModel.RedisDb).GetAwaiter();
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success" }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize]
        public JsonResult StopApplicationPool(string poolName)
        {
            bool isPoolStopedOrStoping;
            try
            {
                isPoolStopedOrStoping = ApplicationPoolService.StopPoolByName(poolName);
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success", poolStatus = isPoolStopedOrStoping }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize]
        public JsonResult StartApplicationPool(string poolName)
        {
            bool isPoolStartedOrStarting;
            try
            {
                isPoolStartedOrStarting = ApplicationPoolService.StartPoolByName(poolName);
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success", poolStatus = isPoolStartedOrStarting }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize]
        [ActionLogger]
        public JsonResult RecycleApplicationPool(string poolName)
        {
            bool isPoolStartedOrStarting;
            try
            {
                isPoolStartedOrStarting = ApplicationPoolService.RecyclePoolByName(poolName);
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success", poolStatus = isPoolStartedOrStarting }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize]
        public JsonResult FlushRedisDb(int redisDb)
        {
            try
            {
                RedisService.FlushDatabaseAsync(redisDb).GetAwaiter();
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success" }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize(Roles = "Admin, Developers")]
        [ActionLogger]
        public JsonResult DeleteApplication(DeleteApplicationDto dto)
        {
            try
            {
                if ((string.IsNullOrEmpty(dto.SiteName) && dto.SiteType == IISSiteType.Application) || string.IsNullOrEmpty(dto.Name))
                {
                    throw new NullReferenceException("Site name or application path is null");
                }

                ApplicationPoolService.DeleteApplicationAsync(dto).GetAwaiter();
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            var redirectUrl = Url.Action("Index", "IIS");
            return Json(new { success = true, responseText = "Success", redirectUrl }, JsonRequestBehavior.AllowGet);
        }

        #endregion

    }
}