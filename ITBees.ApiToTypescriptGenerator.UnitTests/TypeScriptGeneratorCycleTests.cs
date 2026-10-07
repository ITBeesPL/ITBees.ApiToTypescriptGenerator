using NUnit.Framework;

namespace ITBees.ApiToTypescriptGenerator.UnitTests;

[TestFixture]
public class TypeScriptGeneratorCycleTests
{
    public class UserAccount
    {
        public Guid Guid { get; set; }
        public string Email { get; set; } = "";
        public UserAccount? PartnerAccount { get; set; }
    }

    public class CompanyVm
    {
        public string Name { get; set; } = "";
        public List<EmployeeVm> Employees { get; set; } = new();
    }

    public class EmployeeVm
    {
        public string Name { get; set; } = "";
        public CompanyVm? Company { get; set; }
    }

    private static TypeScriptGeneratedModels Generate(Type type) =>
        new TypeScriptGenerator().Generate(type, new TypeScriptGeneratedModels(), false);

    private static string ModelOf(TypeScriptGeneratedModels models, string typeName) =>
        models.GeneratedModels.Single(x => x.TypeName == typeName).Model;

    [Test]
    public void Self_referencing_type_is_generated_once_and_references_itself_by_name()
    {
        var models = Generate(typeof(UserAccount));

        Assert.That(models.GeneratedModels.Select(x => x.TypeName), Is.EqualTo(new[] { "IUserAccount" }));
        var model = ModelOf(models, "IUserAccount");
        Assert.That(model, Does.Contain("partnerAccount?: IUserAccount;"));
        Assert.That(model, Does.Not.Contain("import"));
        Assert.That(model, Does.Not.Contain("error occurred"));
    }

    [Test]
    public void Mutually_referencing_types_are_generated_once_and_import_each_other()
    {
        var models = Generate(typeof(CompanyVm));

        Assert.That(models.GeneratedModels.Select(x => x.TypeName),
            Is.EquivalentTo(new[] { "ICompanyVm", "IEmployeeVm" }));

        var company = ModelOf(models, "ICompanyVm");
        Assert.That(company, Does.Contain("employees?: IEmployeeVm[];"));
        Assert.That(company, Does.Contain("import { IEmployeeVm } from './employee-vm.model';"));

        var employee = ModelOf(models, "IEmployeeVm");
        Assert.That(employee, Does.Contain("company?: ICompanyVm;"));
        Assert.That(employee, Does.Contain("import { ICompanyVm } from './company-vm.model';"));
    }

    [Test]
    public void Cycle_entered_from_the_other_side_gives_the_same_models()
    {
        var models = Generate(typeof(EmployeeVm));

        Assert.That(models.GeneratedModels.Select(x => x.TypeName),
            Is.EquivalentTo(new[] { "ICompanyVm", "IEmployeeVm" }));
        Assert.That(ModelOf(models, "IEmployeeVm"), Does.Contain("company?: ICompanyVm;"));
        Assert.That(ModelOf(models, "ICompanyVm"), Does.Contain("employees?: IEmployeeVm[];"));
    }
}
