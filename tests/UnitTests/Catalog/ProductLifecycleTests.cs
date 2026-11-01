using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// Two things write to a product: an admin, and an unattended import that runs again and again
/// over the same feed. Most of what is asserted here is about keeping those two from undoing
/// each other.
/// </summary>
public sealed class ProductLifecycleTests
{
    private static readonly DateTimeOffset T0 = TestTime.Now;
    private static readonly DateTimeOffset T1 = TestTime.Now.AddHours(1);
    private static readonly DateTimeOffset T2 = TestTime.Now.AddHours(2);

    private static Product Draft(string title = "Demo Kettle") =>
        Product.CreateDraft(1, 10, 20, title, null, null, null, T0);

    /// <summary>A product that is active, with one variant (id 2) and one usable image.</summary>
    private static Product ActiveProduct()
    {
        var product = Draft();
        product.AddVariant(2, "SKU-0002", new Dictionary<string, string>(), T0);
        product.AddMedia(3, "products/1/3.png", 0, "kettle", null, MediaUsageRights.Allowed, T0);
        product.Activate(byAdmin: true, T0);
        return product;
    }

    private static Product ActiveProductWithVariants(params (string Key, string Value)[] variants)
    {
        var product = Draft();
        var id = 2L;
        foreach (var (key, value) in variants)
        {
            product.AddVariant(id++, $"SKU-{id:0000}", new Dictionary<string, string> { [key] = value }, T0);
        }

        product.AddMedia(100, "products/1/100.png", 0, null, null, MediaUsageRights.Allowed, T0);
        product.Activate(byAdmin: true, T0);
        return product;
    }

    [Fact]
    public void Activation_Requires_Active_Variant_Brand_Category_Title_And_Usable_Image()
    {
        var product = Draft();

        product.Slug.Should().Be("demo-kettle-1");
        product.Status.Should().Be(ProductStatus.Draft);
        product.ActivationProblems().Should().BeEquivalentTo("NO_ACTIVE_VARIANT", "NO_USABLE_IMAGE");

        product.AddVariant(2, "SKU-0002", new Dictionary<string, string>(), T0);
        product.AddMedia(3, "products/1/3.png", 0, "kettle", null, MediaUsageRights.RightsUnknown, T0);

        product.Invoking(p => p.Activate(byAdmin: true, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRODUCT_ACTIVATION_INVALID",
                "an image nobody has confirmed the seller may publish is not a usable image");

        product.AddMedia(4, "products/1/4.png", 1, "kettle", null, MediaUsageRights.Allowed, T0);

        product.Activate(byAdmin: true, T0).Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void Activation_Problems_Are_Reported_In_A_Stable_Order()
    {
        var product = Product.CreateDraft(1, 0, 0, "Nameless", null, null, null, T0);

        product.ActivationProblems().Should().Equal(
            "NO_ACTIVE_VARIANT", "NO_BRAND", "NO_PRIMARY_CATEGORY", "NO_USABLE_IMAGE");
    }

    [Fact]
    public void Import_Activation_Never_Clears_Admin_Deactivation()
    {
        var product = ActiveProduct();

        product.Deactivate(byAdmin: true, T1);

        product.Activate(byAdmin: false, T2).Should().BeFalse();
        product.Status.Should().Be(ProductStatus.Deactivated);
        product.DeactivatedByAdmin.Should().BeTrue();

        product.Activate(byAdmin: true, T2).Should().BeTrue();
        product.DeactivatedByAdmin.Should().BeFalse();
    }

    [Fact]
    public void Activating_An_Already_Active_Product_Succeeds_And_Changes_Nothing()
    {
        var product = ActiveProduct();
        product.ClearDomainEvents();

        product.Activate(byAdmin: false, T2).Should().BeTrue();

        product.DomainEvents.Should().BeEmpty(
            "an import re-running over an unchanged feed must not look like a change");
    }

    [Fact]
    public void Import_Variant_Activation_Never_Clears_Admin_Deactivation()
    {
        var product = ActiveProductWithVariants(("size", "M"), ("size", "L"));

        product.SetVariantActive(2, false, byAdmin: true, T1).Should().BeTrue();
        product.SetVariantActive(2, true, byAdmin: false, T2).Should().BeFalse();

        product.Variants.Single(variant => variant.Id == 2).IsActive.Should().BeFalse();
    }

    [Fact]
    public void An_Admin_Can_Re_Enable_A_Variant_They_Disabled()
    {
        var product = ActiveProductWithVariants(("size", "M"), ("size", "L"));
        product.SetVariantActive(2, false, byAdmin: true, T1);

        product.SetVariantActive(2, true, byAdmin: true, T2).Should().BeTrue();

        var variant = product.Variants.Single(candidate => candidate.Id == 2);
        variant.IsActive.Should().BeTrue();
        variant.DeactivatedByAdmin.Should().BeFalse();
    }

    [Fact]
    public void Deactivating_Last_Active_Variant_Deactivates_The_Product_With_Same_Origin()
    {
        var product = ActiveProduct();

        product.SetVariantActive(2, false, byAdmin: false, T1);

        product.Status.Should().Be(ProductStatus.Deactivated);
        product.DeactivatedByAdmin.Should().BeFalse(
            "an import finding nothing in stock must leave the product for the next run to revive");
    }

    [Fact]
    public void An_Admin_Disabling_The_Last_Variant_Pins_The_Product_Off()
    {
        var product = ActiveProduct();

        product.SetVariantActive(2, false, byAdmin: true, T1);

        product.DeactivatedByAdmin.Should().BeTrue(
            "otherwise an import would switch back on the product an admin just emptied");
    }

    [Fact]
    public void Deactivating_One_Of_Several_Variants_Leaves_The_Product_Active()
    {
        var product = ActiveProductWithVariants(("size", "M"), ("size", "L"));

        product.SetVariantActive(2, false, byAdmin: false, T1);

        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void Empty_Signature_Variant_Must_Be_The_Only_Variant()
    {
        var product = Product.CreateDraft(1, 10, 20, "Mug", null, null, null, T0);
        product.AddVariant(2, "SKU-A1", new Dictionary<string, string>(), T0);

        product.Invoking(p => p.AddVariant(3, "SKU-B1", new Dictionary<string, string> { ["size"] = "M" }, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_SIGNATURE_CONFLICT");
    }

    [Fact]
    public void An_Attributed_Variant_Blocks_A_Later_Unattributed_One()
    {
        var product = Product.CreateDraft(1, 10, 20, "Mug", null, null, null, T0);
        product.AddVariant(2, "SKU-A1", new Dictionary<string, string> { ["size"] = "M" }, T0);

        product.Invoking(p => p.AddVariant(3, "SKU-B1", new Dictionary<string, string>(), T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_SIGNATURE_CONFLICT",
                "the rule has to hold whichever order the two arrive in");
    }

    [Fact]
    public void Duplicate_Signature_Is_Rejected()
    {
        var product = Product.CreateDraft(1, 10, 20, "Tee", null, null, null, T0);
        product.AddVariant(2, "SKU-A1", new Dictionary<string, string> { ["Size"] = "M" }, T0);

        product.Invoking(p => p.AddVariant(3, "SKU-A2", new Dictionary<string, string> { ["size"] = " m " }, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_SIGNATURE_DUPLICATE");
    }

    [Fact]
    public void Duplicate_Sku_Within_One_Product_Is_Rejected()
    {
        var product = Product.CreateDraft(1, 10, 20, "Tee", null, null, null, T0);
        product.AddVariant(2, "SKU-A1", new Dictionary<string, string> { ["Size"] = "M" }, T0);

        product.Invoking(p => p.AddVariant(3, "sku-a1", new Dictionary<string, string> { ["Size"] = "L" }, T0))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("VARIANT_SKU_DUPLICATE");
    }

    [Fact]
    public void Merged_Product_Is_Admin_Deactivated_And_Points_To_Survivor()
    {
        var product = ActiveProduct();

        product.MarkMergedInto(99, T1);

        product.Status.Should().Be(ProductStatus.Deactivated);
        product.DeactivatedByAdmin.Should().BeTrue();
        product.MergedIntoProductId.Should().Be(99);

        product.Invoking(p => p.MarkMergedInto(1, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRODUCT_MERGE_INVALID");
    }

    [Fact]
    public void A_Product_Cannot_Be_Merged_Into_Itself() =>
        ActiveProduct().Invoking(p => p.MarkMergedInto(1, T1))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRODUCT_MERGE_INVALID");

    [Fact]
    public void A_Merged_Product_Refuses_Further_Edits()
    {
        var product = ActiveProduct();
        product.MarkMergedInto(99, T1);

        product.Invoking(p => p.UpdateDetails(10, 20, "New Title", null, null, T2))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRODUCT_MERGED",
                "edits belong on the surviving product, or they are lost the moment anyone looks");
    }

    [Fact]
    public void Adding_The_Same_Identifier_Twice_Is_A_No_Op()
    {
        var product = Draft();

        product.AddIdentifier(5, ProductIdentifierType.Ean, "4006381333931", null, T1);
        product.AddIdentifier(6, ProductIdentifierType.Ean, "4006-3813 33931", null, T2);

        product.Identifiers.Should().ContainSingle(
            "an import re-reads the same feed row on every run");
    }

    [Fact]
    public void An_Mpn_Identifier_Also_Sets_The_Denormalized_Column()
    {
        var product = Draft();

        product.AddIdentifier(5, ProductIdentifierType.Mpn, " mx-900 ", null, T1);

        product.Mpn.Should().Be("MX-900");
    }

    [Fact]
    public void A_Second_Different_Mpn_Is_Rejected()
    {
        var product = Draft();
        product.AddIdentifier(5, ProductIdentifierType.Mpn, "MX-900", null, T1);

        product.Invoking(p => p.AddIdentifier(6, ProductIdentifierType.Mpn, "MX-901", null, T2))
            .Should().Throw<DomainException>()
            .Which.Code.Should().Be("PRODUCT_MPN_ALREADY_SET");
    }

    [Fact]
    public void Removing_The_Mpn_Identifier_Clears_The_Column()
    {
        var product = Draft();
        product.AddIdentifier(5, ProductIdentifierType.Mpn, "MX-900", null, T1);

        product.RemoveIdentifier(5, T2);

        product.Mpn.Should().BeNull("the column exists only to index the identifier");
    }

    [Fact]
    public void Source_Refs_Are_Recorded_Once_Each()
    {
        var product = Draft();

        product.AddSourceRef(77, T1);
        product.AddSourceRef(77, T2);
        product.AddSourceRef(78, T2);

        product.SourceRefs.Should().Equal(77L, 78L);
    }

    [Fact]
    public void An_Explicit_Slug_Is_Canonicalised_And_Keeps_No_Id()
    {
        var product = Product.CreateDraft(1, 10, 20, "Demo Kettle", "Classic  Kettle", null, null, T0);

        product.Slug.Should().Be("classic-kettle");
    }

    [Fact]
    public void Renaming_Does_Not_Move_The_Slug()
    {
        var product = Draft();

        product.UpdateDetails(10, 20, "Demo Kettle Mk II", null, null, T1);

        product.Slug.Should().Be("demo-kettle-1",
            "the slug is a URL customers have bookmarked and search engines have indexed");
    }

    [Fact]
    public void Unknown_Variant_And_Media_Ids_Are_Reported()
    {
        var product = ActiveProduct();

        product.Invoking(p => p.SetVariantActive(999, false, byAdmin: true, T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("VARIANT_NOT_FOUND");
        product.Invoking(p => p.RemoveMedia(999, T1))
            .Should().Throw<DomainException>().Which.Code.Should().Be("PRODUCT_MEDIA_NOT_FOUND");
    }

    [Fact]
    public void Deactivating_Raises_An_Event_Carrying_Its_Origin()
    {
        var product = ActiveProduct();
        product.ClearDomainEvents();

        product.Deactivate(byAdmin: true, T1);

        product.DomainEvents.OfType<ProductDeactivatedDomainEvent>().Single()
            .ByAdmin.Should().BeTrue();
    }
}
