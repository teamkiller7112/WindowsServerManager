﻿using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using BLL.Dto;
using BLL.Interfaces;
using DataAccessLayer.Enums;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using WebUI.FullFramework.Core;
using WebUI.FullFramework.Models;

namespace WebUI.FullFramework.Controllers
{
    public class AccountController : Controller
    {
        private IUserService UserService => HttpContext.GetOwinContext().GetUserManager<IUserService>();

        private IAuthenticationManager AuthenticationManager => HttpContext.GetOwinContext().Authentication;

        // localhost/Account/Email/lol2mail.ru
        [Authorize]
        public async Task<ActionResult> Index()
        {
            var users = await UserService.GetUsers();

            return View(users);
        }

        [Authorize]
        public async Task<ActionResult> Edit(string email)
        {
            var user = await UserService.GetUser(email);
            ViewBag.IsOwner = User.Identity.GetUserId() == user.Id;
            ViewBag.IsAdmin = User.IsInRole("Admin");
            ViewBag.ChangePasswordBlockVisible = false;
            return View("User", user);
        }

        public ActionResult Login()
        {
            return View();
        }

        [Authorize]
        public ActionResult Logout()
        {
            AuthenticationManager.SignOut();
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public ActionResult Register()
        {
            return View();
        }

        [Authorize]
        public PartialViewResult ChangePassword(string userId)
        {
            var changeRoleModel = new ChangePasswordModel
            {
                UserId = userId
            };

            ViewBag.ChangePasswordStatus = "";
            return PartialView("_ChangePassword", changeRoleModel);
        }

        [ChildActionOnly]
        [Authorize]
        public PartialViewResult ChangeUserRole(string userId, string email)
        {
            var model = new ChangeRoleModel
            {
                UserId = userId,
                Email = email
            };

            return PartialView("_ChangeRole", model);
        }

        #region Methods: API

        [HttpPost]
        [Authorize]
        [ActionLogger]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserDTO editModel)
        {
            var isAdmin = User.IsInRole("Admin");
            var currentUserId = User.Identity.GetUserId();

            if (ModelState.IsValid)
            {
                if (editModel.Id != currentUserId && !isAdmin)
                {
                    ModelState.AddModelError("", @"У вас нет прав на редактирование данной записи");
                    ViewBag.IsAdmin = false;
                    ViewBag.IsOwner = currentUserId == editModel.Id;
                    return View("User", editModel);
                }

                await UserService.UpdateUser(editModel, isAdmin);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginModel model)
        {
            if (ModelState.IsValid)
            {
                var isEnabled = await UserService.IsUserEnabled(model.Email);

                if (isEnabled.HasValue && !isEnabled.Value || !isEnabled.HasValue)
                {
                    ModelState.AddModelError("", @"Ваша учетная запись была деактивирована");
                    return View(model);
                }

                UserDTO userDto = new UserDTO { Email = model.Email, Password = model.Password };
                ClaimsIdentity claim = await UserService.Authenticate(userDto);
                if (claim == null)
                {
                    ModelState.AddModelError("", "Неверный логин или пароль.");
                }
                else
                {
                    AuthenticationManager.SignOut();
                    AuthenticationManager.SignIn(new AuthenticationProperties
                    {
                        IsPersistent = true
                    }, claim);
                    HttpContext.Session.Add("UserEmail", model.Email);
                    return RedirectToAction("Index", "Home");
                }
            }
            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<PartialViewResult> ChangePassword(ChangePasswordModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await UserService.ChangePassword(model.UserId, model.OldPassword, model.NewPassword);
                model = new ChangePasswordModel
                {
                    UserId = model.UserId
                };
                if (result.Succedeed)
                {
                    ViewBag.ChangePasswordStatus = "Success";
                    return PartialView("_ChangePassword", model);
                }
                ModelState.AddModelError("", result.Message);
            }

            ViewBag.ChangePasswordBlockVisible = true;
            ViewBag.ChangePasswordStatus = "Fail";
            model = new ChangePasswordModel
            {
                UserId = model.UserId
            };
            return PartialView("_ChangePassword", model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ActionLogger]
        [ValidateAntiForgeryToken]
        public async Task<PartialViewResult> ChangeUserRole(ChangeRoleModel changeRoleModel)
        {
            if (ModelState.IsValid)
            {
                var result = await UserService.ChangeUserRole(changeRoleModel.Role, changeRoleModel.UserId);

                if (result.Succedeed)
                {
                    ViewBag.ChangeRoleStatus = "Success";
                    return PartialView("_ChangeRole", changeRoleModel);
                }
                ModelState.AddModelError(result.Property, result.Message);
            }

            ViewBag.ChangeRoleStatus = "Fail";
            return PartialView("_ChangeRole", changeRoleModel);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterModel model)
        {
            if (ModelState.IsValid)
            {
                var userDto = new UserDTO
                {
                    Email = model.Email,
                    Password = model.Password,
                    Name = model.Name,
                    Role = model.Role.ToString()
                };
                var operationDetails = await UserService.Create(userDto);
                if (operationDetails.Succedeed)
                {
                    return RedirectToAction("Index");
                }
                ModelState.AddModelError(operationDetails.Property, operationDetails.Message);
            }
            return View(model);
        }

        [HttpPost]
        [ActionLogger]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> SetUserEnableStatus(string id, bool status)
        {
            try
            {
                await UserService.ChangeIsEnabled(status, id);
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success" }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ActionLogger]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> DeleteUser(string id)
        {
            try
            {
                await UserService.DeleteUser(id);
            }
            catch (Exception e)
            {
                NLogger.Log(e);
                return Json(new { success = false, responseText = $"{e.Message}" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { success = true, responseText = $"Success" }, JsonRequestBehavior.AllowGet);
        }

        #endregion
    }
}