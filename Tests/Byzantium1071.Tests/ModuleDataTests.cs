using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class ModuleDataTests
    {
        private static readonly Regex AtBindingPattern = new(@"^@(?<name>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);
        private static readonly Regex DataSourcePattern = new(@"^\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.Compiled);
        private static readonly Regex ViewModelPropertyPattern = new(
            @"public\s+[A-Za-z0-9_<>,.?\[\] ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{",
            RegexOptions.Compiled);

        public static IEnumerable<object[]> PrefabViewModelSources()
        {
            yield return new object[]
            {
                "B1071_Demobilization.xml",
                new[] { "B1071_DemobilizationVM.cs", "B1071_DemobilizationCohortVM.cs" }
            };
            yield return new object[]
            {
                "B1071_SlaveConversion.xml",
                new[] { "B1071_SlaveConversionVM.cs", "B1071_SlaveConversionTroopVM.cs" }
            };
            yield return new object[]
            {
                "B1071_VeteranRecall.xml",
                new[] { "B1071_VeteranRecallVM.cs", "B1071_VeteranRecallTroopVM.cs", "B1071_VeteranRecallTransitVM.cs" }
            };
            yield return new object[]
            {
                "B1071_CastleRecruitment.xml",
                new[] { "B1071_CastleRecruitmentVM.cs", "B1071_CastleRecruitTroopVM.cs" }
            };
        }

        [Fact]
        public void ModuleXmlFilesAreWellFormed()
        {
            string[] xmlFiles =
            {
                RepositoryPaths.FromRoot("_Module", "SubModule.xml"),
                RepositoryPaths.FromRoot("_Module", "ModuleData", "items.xml"),
                RepositoryPaths.FromRoot("_Module", "ModuleData", "item_categories.xml")
            };

            foreach (object[] prefab in PrefabViewModelSources())
            {
                xmlFiles = xmlFiles.Append(RepositoryPaths.FromRoot("_Module", "GUI", "Prefabs", (string)prefab[0])).ToArray();
            }

            foreach (string xmlFile in xmlFiles)
            {
                Assert.NotNull(XDocument.Load(xmlFile).Root);
            }
        }

        [Fact]
        public void CustomItemCategoriesExist()
        {
            XDocument items = XDocument.Load(RepositoryPaths.FromRoot("_Module", "ModuleData", "items.xml"));
            XDocument categories = XDocument.Load(RepositoryPaths.FromRoot("_Module", "ModuleData", "item_categories.xml"));

            string[] referenced = items.Descendants("Item")
                .Select(item => (string?)item.Attribute("item_category"))
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            HashSet<string> defined = categories.Descendants("ItemCategory")
                .Select(category => (string?)category.Attribute("id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            string[] missing = referenced.Where(category => !defined.Contains(category)).OrderBy(category => category).ToArray();
            Assert.True(missing.Length == 0, $"Undefined item categories: {string.Join(", ", missing)}");
        }

        [Theory]
        [MemberData(nameof(PrefabViewModelSources))]
        public void PrefabBindingsExistOnTheirViewModels(string prefabName, string[] viewModelSources)
        {
            XDocument prefab = XDocument.Load(RepositoryPaths.FromRoot("_Module", "GUI", "Prefabs", prefabName));
            HashSet<string> knownProperties = new(StringComparer.Ordinal);

            foreach (string sourceName in viewModelSources)
            {
                string source = File.ReadAllText(RepositoryPaths.FromRoot("Campaign", "UI", sourceName));
                foreach (Match match in ViewModelPropertyPattern.Matches(source))
                {
                    knownProperties.Add(match.Groups["name"].Value);
                }
            }

            string[] bindings = prefab.Descendants()
                .Attributes()
                .SelectMany(attribute => BoundNames(attribute.Value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] missing = bindings.Where(binding => !knownProperties.Contains(binding)).ToArray();

            Assert.True(
                missing.Length == 0,
                $"{prefabName} references missing view-model properties: {string.Join(", ", missing)}");
        }

        private static IEnumerable<string> BoundNames(string value)
        {
            Match atBinding = AtBindingPattern.Match(value);
            if (atBinding.Success)
            {
                yield return atBinding.Groups["name"].Value;
            }

            Match dataSource = DataSourcePattern.Match(value);
            if (dataSource.Success)
            {
                yield return dataSource.Groups["name"].Value;
            }
        }
    }
}
