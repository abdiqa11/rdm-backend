using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RdmApi.Contracts.Datasets;
using RdmApi.Controllers;
using RdmApi.Data;
using RdmApi.Data.Entities;
using RdmApi.Security;
using RdmApi.Services;
using Xunit;

namespace RdmApi.Tests;

public class RbacContractTests
{
    [Fact]
    public async Task ReadEndpoints_ViewerResearcherAdmin_AreAccessible()
    {
        await using var db = CreateDb();
        var dataset = new Dataset { Title = "DS", Creator = "creator", OwnerId = "owner-sub" };
        db.Datasets.Add(dataset);
        await db.SaveChangesAsync();

        var viewer = CreateDatasetsController(db, Roles.Viewer, "viewer", "viewer-sub");
        var researcher = CreateDatasetsController(db, Roles.Researcher, "researcher", "researcher-sub");
        var admin = CreateDatasetsController(db, Roles.Admin, "admin", "admin-sub");

        Assert.IsType<OkObjectResult>(await viewer.Search(null, null, null, 20, 0, default));
        Assert.IsType<OkObjectResult>(await researcher.Search(null, null, null, 20, 0, default));
        Assert.IsType<OkObjectResult>(await admin.Search(null, null, null, 20, 0, default));

        Assert.IsType<OkObjectResult>(await viewer.GetById(dataset.Id, default));
        Assert.IsType<OkObjectResult>(await researcher.GetById(dataset.Id, default));
        Assert.IsType<OkObjectResult>(await admin.GetById(dataset.Id, default));
    }

    [Fact]
    public async Task DownloadEndpoints_Viewer_AreNotForbidden_WhenReachable()
    {
        await using var db = CreateDb();
        var datasetId = Guid.NewGuid();
        var viewer = CreateDatasetsController(db, Roles.Viewer, "viewer", "viewer-sub");

        var fileDownload = await viewer.DownloadVersionFile(datasetId, 1, "file.txt", default);
        var zipDownload = await viewer.DownloadVersionAsZip(datasetId, 1, default);

        Assert.IsNotType<ForbidResult>(fileDownload);
        Assert.IsNotType<ForbidResult>(zipDownload);
        Assert.IsType<NotFoundObjectResult>(fileDownload);
        Assert.IsType<NotFoundObjectResult>(zipDownload);
    }

    [Fact]
    public async Task DatasetCreation_ResearcherAndAdminAllowed_ViewerDenied()
    {
        await using var db = CreateDb();
        var request = new CreateDatasetRequest("Created DS", "Creator", "desc");

        var viewer = CreateDatasetsController(db, Roles.Viewer, "viewer", "viewer-sub");
        var researcher = CreateDatasetsController(db, Roles.Researcher, "researcher", "researcher-sub");
        var admin = CreateDatasetsController(db, Roles.Admin, "admin", "admin-sub");

        var viewerGate = await EvaluateRequireRoleAsync(viewer, nameof(DatasetsController.Create));
        Assert.NotNull(viewerGate);
        Assert.IsType<ObjectResult>(viewerGate);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)viewerGate).StatusCode);

        var researcherGate = await EvaluateRequireRoleAsync(researcher, nameof(DatasetsController.Create));
        Assert.Null(researcherGate);
        var researcherCreate = await researcher.Create(request, default);
        Assert.IsType<CreatedResult>(researcherCreate.Result);

        var adminGate = await EvaluateRequireRoleAsync(admin, nameof(DatasetsController.Create));
        Assert.Null(adminGate);
        var adminCreate = await admin.Create(request, default);
        Assert.IsType<CreatedResult>(adminCreate.Result);
    }

    [Fact]
    public async Task OwnershipBasedModification_ResearcherOwnerAllowed_ResearcherOtherDenied_AdminAllowed()
    {
        await using var db = CreateDb();
        var ownedDataset = new Dataset
        {
            Id = Guid.NewGuid(),
            Title = "Owned",
            Creator = "creator",
            OwnerId = "owner-sub"
        };
        db.Datasets.Add(ownedDataset);
        await db.SaveChangesAsync();

        var owner = CreateDatasetsController(db, Roles.Researcher, "owner@uia.no", "owner-sub");
        var otherResearcher = CreateDatasetsController(db, Roles.Researcher, "other@uia.no", "other-sub");
        var admin = CreateDatasetsController(db, Roles.Admin, "admin@uia.no", "admin-sub");

        var ownerUpdate = await owner.Update(ownedDataset.Id, new UpdateDatasetRequest("Updated title", "updated"), default);
        var ownerUpload = await owner.UploadVersion(ownedDataset.Id, CreateOwnershipTestUploadRequest(), default);
        var ownerAnnotation = await owner.CreateAnnotation(ownedDataset.Id, new CreateAnnotationRequest("note"), default);

        Assert.IsType<OkObjectResult>(ownerUpdate);
        Assert.IsType<BadRequestObjectResult>(ownerUpload);
        Assert.IsType<OkObjectResult>(ownerAnnotation);

        Assert.IsType<ForbidResult>(await otherResearcher.Update(ownedDataset.Id, new UpdateDatasetRequest("Nope", null), default));
        Assert.IsType<ForbidResult>(await otherResearcher.UploadVersion(ownedDataset.Id, CreateOwnershipTestUploadRequest(), default));
        Assert.IsType<ForbidResult>(await otherResearcher.CreateAnnotation(ownedDataset.Id, new CreateAnnotationRequest("blocked"), default));

        var adminUpdate = await admin.Update(ownedDataset.Id, new UpdateDatasetRequest("Admin update", "ok"), default);
        var adminUpload = await admin.UploadVersion(ownedDataset.Id, CreateOwnershipTestUploadRequest(), default);
        var adminAnnotation = await admin.CreateAnnotation(ownedDataset.Id, new CreateAnnotationRequest("admin note"), default);

        Assert.IsType<OkObjectResult>(adminUpdate);
        Assert.IsType<BadRequestObjectResult>(adminUpload);
        Assert.IsType<OkObjectResult>(adminAnnotation);
    }

    [Fact]
    public async Task LegacyNullOwner_ResearcherDenied_AdminAllowed()
    {
        await using var db = CreateDb();
        var legacyDataset = new Dataset
        {
            Id = Guid.NewGuid(),
            Title = "Legacy",
            Creator = "legacy-creator",
            OwnerId = null
        };
        db.Datasets.Add(legacyDataset);
        await db.SaveChangesAsync();

        var researcher = CreateDatasetsController(db, Roles.Researcher, "res@uia.no", "res-sub");
        var admin = CreateDatasetsController(db, Roles.Admin, "admin@uia.no", "admin-sub");

        Assert.IsType<ForbidResult>(await researcher.Update(legacyDataset.Id, new UpdateDatasetRequest("x", "y"), default));
        Assert.IsType<ForbidResult>(await researcher.UploadVersion(legacyDataset.Id, CreateOwnershipTestUploadRequest(), default));

        Assert.IsType<OkObjectResult>(await admin.Update(legacyDataset.Id, new UpdateDatasetRequest("admin", "ok"), default));
        Assert.IsType<BadRequestObjectResult>(await admin.UploadVersion(legacyDataset.Id, CreateOwnershipTestUploadRequest(), default));
    }

    [Fact]
    public async Task AdminOnlyGovernanceEndpoints_ViewerAndResearcherDenied_AdminAllowed()
    {
        await using var db = CreateDb();

        var ds1 = new Dataset { Id = Guid.NewGuid(), Title = "A", Creator = "c1", OwnerId = "owner-a" };
        var ds2 = new Dataset { Id = Guid.NewGuid(), Title = "B", Creator = "c2", OwnerId = "owner-b" };
        db.Datasets.AddRange(ds1, ds2);
        db.AuditEvents.Add(new AuditEvent { Actor = "system", Action = "SEED", DatasetId = ds1.Id });
        await db.SaveChangesAsync();

        var viewer = CreateDatasetsController(db, Roles.Viewer, "viewer", "viewer-sub");
        var researcher = CreateDatasetsController(db, Roles.Researcher, "owner@uia.no", "owner-a");
        var admin = CreateDatasetsController(db, Roles.Admin, "admin@uia.no", "admin-sub");

        var viewerAuditGate = await EvaluateRequireRoleAsync(CreateAuditController(db, Roles.Viewer, "viewer", "viewer-sub"), nameof(AuditController.List));
        var researcherAuditGate = await EvaluateRequireRoleAsync(CreateAuditController(db, Roles.Researcher, "owner@uia.no", "owner-a"), nameof(AuditController.List));
        var adminAuditGate = await EvaluateRequireRoleAsync(CreateAuditController(db, Roles.Admin, "admin@uia.no", "admin-sub"), nameof(AuditController.List));
        Assert403(viewerAuditGate);
        Assert403(researcherAuditGate);
        Assert.Null(adminAuditGate);

        var viewerDatasetAuditGate = await EvaluateRequireRoleAsync(viewer, nameof(DatasetsController.GetDatasetAudit));
        var researcherDatasetAuditGate = await EvaluateRequireRoleAsync(researcher, nameof(DatasetsController.GetDatasetAudit));
        var adminDatasetAuditGate = await EvaluateRequireRoleAsync(admin, nameof(DatasetsController.GetDatasetAudit));
        Assert403(viewerDatasetAuditGate);
        Assert403(researcherDatasetAuditGate);
        Assert.Null(adminDatasetAuditGate);

        var viewerStatusGate = await EvaluateRequireRoleAsync(viewer, nameof(DatasetsController.UpdateStatus));
        var researcherStatusGate = await EvaluateRequireRoleAsync(researcher, nameof(DatasetsController.UpdateStatus));
        var adminStatusGate = await EvaluateRequireRoleAsync(admin, nameof(DatasetsController.UpdateStatus));
        Assert403(viewerStatusGate);
        Assert403(researcherStatusGate);
        Assert.Null(adminStatusGate);

        var viewerRelationshipGate = await EvaluateRequireRoleAsync(viewer, nameof(DatasetsController.CreateRelationship));
        var researcherRelationshipGate = await EvaluateRequireRoleAsync(researcher, nameof(DatasetsController.CreateRelationship));
        var adminRelationshipGate = await EvaluateRequireRoleAsync(admin, nameof(DatasetsController.CreateRelationship));
        Assert403(viewerRelationshipGate);
        Assert403(researcherRelationshipGate);
        Assert.Null(adminRelationshipGate);

        Assert.IsType<OkObjectResult>(await CreateAuditController(db, Roles.Admin, "admin@uia.no", "admin-sub").List());
        Assert.IsType<OkObjectResult>(await admin.GetDatasetAudit(ds1.Id, 10, default));
    }

    private static void Assert403(IActionResult? result)
    {
        Assert.NotNull(result);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    private static async Task<IActionResult?> EvaluateRequireRoleAsync(ControllerBase controller, string methodName)
    {
        var method = controller.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        var filters = method!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: true)
            .Cast<RequireRoleAttribute>()
            .ToList();

        if (filters.Count == 0)
            return null;

        var actionContext = new ActionContext(
            controller.HttpContext,
            new RouteData(),
            new ControllerActionDescriptor());

        var executingContext = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller);

        ActionExecutionDelegate next = () =>
            Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller));

        foreach (var filter in filters)
        {
            await filter.OnActionExecutionAsync(executingContext, next);
            if (executingContext.Result is not null)
                return executingContext.Result;
        }

        return null;
    }

    private static DatasetsController CreateDatasetsController(
        RdmDbContext db,
        string role,
        string actor,
        string sub)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S3:Endpoint"] = "localhost:9000",
                ["S3:AccessKey"] = "test",
                ["S3:SecretKey"] = "test",
                ["S3:Bucket"] = "test",
                ["S3:UseSsl"] = "false"
            })
            .Build();

        var controller = new DatasetsController(
            db,
            new S3ObjectStore(cfg),
            new DatasetOwnershipAuthorizer());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(db, role, actor, sub)
        };

        return controller;
    }

    private static AuditController CreateAuditController(
        RdmDbContext db,
        string role,
        string actor,
        string sub)
    {
        var controller = new AuditController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(db, role, actor, sub)
        };
        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(RdmDbContext db, string role, string actor, string sub)
    {
        var services = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services
        };

        context.Items["role"] = role;
        context.Items["actor"] = actor;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", sub),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim(ClaimTypes.Email, actor)
        }, "Tests"));

        return context;
    }

    private static RdmDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RdmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new RdmDbContext(options);
    }

    private static UploadDatasetVersionRequest CreateOwnershipTestUploadRequest()
    {
        return new UploadDatasetVersionRequest
        {
            RemovedPaths = new List<string> { "ghost.txt" }
        };
    }
}
