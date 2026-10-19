using System.Reflection;
using LevBakehila.API.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace LevBakehila.Tests.Unit;

// Verifies that role-based authorization is declared on the server side for every protected endpoint
public class AuthorizationAttributeTests
{
    [Theory]
    [InlineData(typeof(ActivitiesController), nameof(ActivitiesController.Create))]
    [InlineData(typeof(ActivitiesController), nameof(ActivitiesController.Update))]
    [InlineData(typeof(ActivitiesController), nameof(ActivitiesController.Cancel))]
    [InlineData(typeof(UsersController), nameof(UsersController.GetAll))]
    [InlineData(typeof(UsersController), nameof(UsersController.GetById))]
    [InlineData(typeof(UsersController), nameof(UsersController.Delete))]
    [InlineData(typeof(ActivitiesController), nameof(ActivitiesController.GetRegistrations))]
    [InlineData(typeof(SkillsController), nameof(SkillsController.Create))]
    [InlineData(typeof(SkillsController), nameof(SkillsController.Update))]
    [InlineData(typeof(SkillsController), nameof(SkillsController.Delete))]
    public void AdminEndpoints_RequireAdminRole(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        var attributes = method.GetCustomAttributes<AuthorizeAttribute>();

        Assert.Contains(attributes, a => a.Roles == "Admin");
    }

    [Fact]
    public void RegisterToActivity_RequiresVolunteerRole()
    {
        var method = typeof(RegistrationsController).GetMethod(nameof(RegistrationsController.Register))!;
        Assert.Contains(method.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == "Volunteer");
    }

    [Theory]
    [InlineData(typeof(ActivitiesController))]
    [InlineData(typeof(UsersController))]
    [InlineData(typeof(RegistrationsController))]
    [InlineData(typeof(SkillsController))]
    public void ProtectedControllers_RequireAuthentication(Type controller)
    {
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
