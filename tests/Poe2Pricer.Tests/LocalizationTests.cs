using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Poe2Pricer.Core;
using Xunit;

namespace Poe2Pricer.Tests;

// Texts.Language is process-wide. Keep these tests apart from other collections,
// and restore its original value after each test, including failed assertions.
[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;

[Collection("Localization")]
public sealed class LocalizationTests : IDisposable
{
    private readonly string originalLanguage = Texts.Language;
    private static readonly ResourceManager Resources =
        new("Poe2Pricer.Core.Languages.Strings", typeof(Texts).Assembly);

    public void Dispose() => Texts.Language = originalLanguage;

    [Fact]
    public void RussianCanBeSelectedWithLocalizedResourcesAndNumbers()
    {
        Texts.Language = "ru";
        Assert.Equal("ru", Texts.Language);
        Assert.Equal("Сохранить", Texts.Get("Save"));
        Assert.Equal(",", Texts.DecimalSeparator);
    }

    [Theory]
    [InlineData(0, "предметов")]
    [InlineData(1, "предмет")]
    [InlineData(2, "предмета")]
    [InlineData(4, "предмета")]
    [InlineData(5, "предметов")]
    [InlineData(11, "предметов")]
    [InlineData(12, "предметов")]
    [InlineData(14, "предметов")]
    [InlineData(21, "предмет")]
    [InlineData(22, "предмета")]
    [InlineData(25, "предметов")]
    [InlineData(101, "предмет")]
    [InlineData(111, "предметов")]
    [InlineData(112, "предметов")]
    [InlineData(-1, "предмет")]
    [InlineData(-22, "предмета")]
    [InlineData(int.MinValue, "предметов")]
    [InlineData(int.MaxValue, "предметов")]
    public void RussianCountsUseCorrectPluralForm(int count, string noun)
    {
        Texts.Language = "ru";
        var expected = count.ToString("N0", CultureInfo.GetCultureInfo("ru")) + " " + noun;
        Assert.Equal(expected, Texts.Count("Items", count));
        Assert.Equal(expected, Texts.CountIn("ru", "Items", count));
        Assert.Equal(expected, Texts.CountIn("ru-RU", "Items", count));
    }

    [Fact]
    public void CountedMessagesPreserveCountAndAdditionalArguments()
    {
        Texts.Language = "ru";
        var singular = Texts.Count("AllBelowThreshold", 21, "10", "ex");
        Assert.Contains("21 предмет", singular);
        Assert.Contains("10", singular);
        Assert.Contains("ex", singular);
        var few = Texts.Count("StashAllBelowThreshold", 22, "10", "ex", "Orbs");
        Assert.Contains("22 предмета", few);
        Assert.Contains("10", few);
        Assert.Contains("ex", few);
        Assert.Contains("Orbs", few);
    }

    [Fact]
    public void EnglishLogTextsStayEnglishWhenRussianIsSelected()
    {
        Texts.Language = "ru";
        Assert.Equal("Save", Texts.English("Save"));
    }

    [Fact]
    public void MissingKeysAreShownInBrackets()
    {
        Texts.Language = "ru";
        Assert.Equal("[DoesNotExist]", Texts.Get("DoesNotExist"));
    }

    [Theory]
    [InlineData("en", "1 item", "21 items")]
    [InlineData("de", "1 Gegenstand", "21 Gegenstände")]
    public void EnglishAndGermanPluralFormsAreUnchanged(string language, string one, string many)
    {
        Texts.Language = language;
        Assert.Equal(one, Texts.Count("Items", 1));
        Assert.Equal(many, Texts.Count("Items", 21));
    }

    [Fact]
    public void UnsupportedUiLanguageFallsBackToEnglish()
    {
        Texts.Language = "fr";
        Assert.Equal("en", Texts.Language);
    }

    [Fact]
    public void InvalidExplicitCultureFallsBackToEnglish()
    {
        Assert.Equal("2 items", Texts.CountIn("invalid_language!", "Items", 2));
    }

    [Fact]
    public void RussianSatelliteResourcesExistWithoutEnglishFallback()
    {
        Assert.NotNull(Resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false));
    }

    public static IEnumerable<object[]> EnglishResourceKeys()
    {
        var english = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        Assert.NotNull(english);
        return english.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => new object[] { key });
    }

    [Theory]
    [MemberData(nameof(EnglishResourceKeys))]
    public void RussianTranslationsPreserveEveryEnglishKeyAndFormat(string key)
    {
        var english = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var russian = Resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false);
        Assert.NotNull(english);
        Assert.NotNull(russian);
        var source = english.GetString(key);
        Assert.NotNull(source);
        AssertTranslation(source, russian.GetString(key), key.EndsWith("_one", StringComparison.Ordinal));
        if (key.EndsWith("_other", StringComparison.Ordinal))
            AssertTranslation(source, russian.GetString(key[..^6] + "_few"), false);
    }

    private static void AssertTranslation(string source, string? translated, bool allowsAddedCount)
    {
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.NotNull(translated);
        var sourceFormat = CompositeFormat.Parse(source);
        var targetFormat = CompositeFormat.Parse(translated);
        var sourceSlots = Regex.Matches(source, @"\{\d+[^}]*\}").Select(m => m.Value).ToHashSet();
        var targetSlots = Regex.Matches(translated, @"\{\d+[^}]*\}").Select(m => m.Value).ToHashSet();
        // Russian singular also serves 21, 31, ... and may need the count
        // where English says "the only item" without a numeric placeholder.
        if (allowsAddedCount)
            targetSlots.RemoveWhere(slot => (slot is "{0}" or "{0:N0}") && !sourceSlots.Contains(slot));
        Assert.Equal(sourceSlots.OrderBy(slot => slot), targetSlots.OrderBy(slot => slot));
        var values = Enumerable.Repeat<object>(123,
            Math.Max(sourceFormat.MinimumArgumentCount, targetFormat.MinimumArgumentCount)).ToArray();
        _ = string.Format(CultureInfo.GetCultureInfo("ru"), translated, values);
    }
}
