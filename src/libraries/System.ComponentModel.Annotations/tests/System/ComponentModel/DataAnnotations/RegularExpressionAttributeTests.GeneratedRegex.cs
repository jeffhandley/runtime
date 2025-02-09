// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace System.ComponentModel.DataAnnotations.Tests
{
    public sealed partial class RegularExpressionAttributeTests_GeneratedRegex
    {
        [Fact]
        public static void RegexType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>("regexType", () => new RegularExpressionAttribute(null!, "Method"));
        }

        [Fact]
        public static void RegexMethodName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>("regexMethodName", () => new RegularExpressionAttribute(typeof(PublicType), null!));
            Assert.Throws<ArgumentException>("regexMethodName", () => new RegularExpressionAttribute(typeof(PublicType), string.Empty));
            Assert.Throws<ArgumentException>("regexMethodName", () => new RegularExpressionAttribute(typeof(PublicType), " "));
        }

        [Theory]
        [InlineData(typeof(InvalidMethods), "NonexistentMethod")]
        [InlineData(typeof(InvalidMethods), nameof(InvalidMethods.InstanceMethod))]
        [InlineData(typeof(InvalidMethods), nameof(InvalidMethods.Nullable))]
        [InlineData(typeof(InvalidMethods), nameof(InvalidMethods.ObjectMethod))]
        [InlineData(typeof(InvalidMethods), nameof(InvalidMethods.Throws))]
        public static void RegexMethodError_ThrowsInvalidOperationException_FromIsValid(Type regexType, string regexMethodName)
        {
            RegularExpressionAttribute attribute = new(regexType, regexMethodName);
            Assert.Throws<InvalidOperationException>(() => attribute.IsValid("input"));
        }

        [Theory]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegex), "Valid", true)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegex), "Invalid", false)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseWithTimeout100), "vAlId", true)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseWithTimeout100), "Invalid", false)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseNoTimeout), "VaLiD", true)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseNoTimeout), "Invalid", false)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetDerivedRegex), "Valid", true)]
        [InlineData(typeof(PublicType), nameof(PublicType.GetDerivedRegex), "Invalid", false)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegex, "Valid", true)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegex, "Invalid", false)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseWithTimeout200, "vAlId", true)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseWithTimeout200, "Invalid", false)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseNoTimeout, "VaLiD", true)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseNoTimeout, "Invalid", false)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegex), "Valid", true)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegex), "Invalid", false)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseWithTimeout300), "vAlId", true)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseWithTimeout300), "Invalid", false)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseNoTimeout), "VaLiD", true)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseNoTimeout), "Invalid", false)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegex, "Valid", true)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegex, "Invalid", false)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseWithTimeout400, "vAlId", true)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseWithTimeout400, "Invalid", false)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseNoTimeout, "VaLiD", true)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseNoTimeout, "Invalid", false)]
        public static void GeneratedRegex_MatchesExpected(Type regexType, string regexMember, string input, bool expected)
        {
            RegularExpressionAttribute attribute = new(regexType, regexMember);
            Assert.Equal(expected, attribute.IsValid(input));
        }

        [Theory]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseWithTimeout100), 100)]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseWithTimeout200, 200)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseWithTimeout300), 300)]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseWithTimeout400, 400)]
        public static void GeneratedRegex_RespectsTimeoutWhenNotInfinite(Type regexType, string regexMember, int timeout)
        {
            RegularExpressionAttribute attribute = new(regexType, regexMember);
            Assert.Equal(timeout, attribute.MatchTimeoutInMilliseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(timeout), attribute.MatchTimeout);
        }

        [Theory]
        [InlineData(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseNoTimeout))]
        [InlineData(typeof(PublicType), PublicType.PrivateRegexIgnoreCaseNoTimeout)]
        [InlineData(typeof(PrivateType), nameof(PrivateType.GetPublicRegexIgnoreCaseNoTimeout))]
        [InlineData(typeof(PrivateType), PrivateType.PrivateRegexIgnoreCaseNoTimeout)]
        public static void GeneratedRegex_AppliesDefaultTimeoutWhenInfinite(Type regexType, string regexMember)
        {
            RegularExpressionAttribute attribute = new(regexType, regexMember);
            Assert.Equal(2000, attribute.MatchTimeoutInMilliseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(2000), attribute.MatchTimeout);
        }

        [Fact]
        public static void MatchTimeoutMilliseconds_OverridesRegex()
        {
            RegularExpressionAttribute attribute = new(typeof(PublicType), nameof(PublicType.GetPublicRegexIgnoreCaseWithTimeout100))
            {
                MatchTimeoutInMilliseconds = 456
            };

            Assert.Equal(456, attribute.MatchTimeoutInMilliseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(456), attribute.MatchTimeout);
        }

        public static partial class PublicType
        {
            public const string PrivateRegex = nameof(GetPrivateRegex);
            public const string PrivateRegexIgnoreCaseWithTimeout200 = nameof(GetPrivateRegexIgnoreCaseWithTimeout200);
            public const string PrivateRegexIgnoreCaseNoTimeout = nameof(GetPrivateRegexIgnoreCaseNoTimeout);

            [GeneratedRegex("Valid")]
            public static partial Regex GetPublicRegex();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, 100)]
            public static partial Regex GetPublicRegexIgnoreCaseWithTimeout100();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, -1)]
            public static partial Regex GetPublicRegexIgnoreCaseNoTimeout();

            [GeneratedRegex("Valid")]
            private static partial Regex GetPrivateRegex();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, 200)]
            private static partial Regex GetPrivateRegexIgnoreCaseWithTimeout200();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, -1)]
            private static partial Regex GetPrivateRegexIgnoreCaseNoTimeout();

            public static DerivedRegex GetDerivedRegex() => new("Valid");

            public class DerivedRegex : Regex
            {
                public DerivedRegex(string pattern) : base(pattern) { }
            }
        }

        private static partial class PrivateType
        {
            public const string PrivateRegex = nameof(GetPrivateRegex);
            public const string PrivateRegexIgnoreCaseWithTimeout400 = nameof(GetPrivateRegexIgnoreCaseWithTimeout400);
            public const string PrivateRegexIgnoreCaseNoTimeout = nameof(GetPrivateRegexIgnoreCaseNoTimeout);

            [GeneratedRegex("Valid")]
            public static partial Regex GetPublicRegex();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, 300)]
            public static partial Regex GetPublicRegexIgnoreCaseWithTimeout300();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, -1)]
            public static partial Regex GetPublicRegexIgnoreCaseNoTimeout();

            [GeneratedRegex("Valid")]
            private static partial Regex GetPrivateRegex();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, 400)]
            private static partial Regex GetPrivateRegexIgnoreCaseWithTimeout400();

            [GeneratedRegex("Valid", RegexOptions.IgnoreCase, -1)]
            private static partial Regex GetPrivateRegexIgnoreCaseNoTimeout();
        }

        public class InvalidMethods
        {
            public Regex InstanceMethod() => null!;
            public static Regex? Nullable() => null;
            public static object ObjectMethod() => null!;
            public static Regex Throws() => throw new Exception();
        }
    }
}
