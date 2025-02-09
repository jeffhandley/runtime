// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace System.ComponentModel.DataAnnotations
{
    /// <summary>
    ///     Regular expression validation attribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false)]
    public class RegularExpressionAttribute : ValidationAttribute
    {
        /// <summary>
        ///     Constructor that accepts the regular expression pattern
        /// </summary>
        /// <param name="pattern">The regular expression to use.  It cannot be null.</param>
        public RegularExpressionAttribute([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            : base(() => SR.RegexAttribute_ValidationError)
        {
            Pattern = pattern;
            MatchTimeoutInMilliseconds = 2000;
        }

        /// <summary>
        /// Create a <see cref="RegularExpressionAttribute"/> using a <see cref="Regex"/> returned from the specified type and method name.
        /// </summary>
        /// <param name="regexType">The type that contains the method returning a <see cref="Regex"/>.</param>
        /// <param name="regexMethodName">The method name that returns the <see cref="Regex"/>. The method must be static and accept no arguments.</param>
        /// <exception cref="ArgumentNullException">When the <paramref name="regexType"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">When the <paramref name="regexMethodName"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">When the <paramref name="regexMethodName"/> is empty or consists only of white-space characters.</exception>
        public RegularExpressionAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.AllMethods)] Type regexType, string regexMethodName)
            : this(string.Empty)
        {
            ArgumentNullException.ThrowIfNull(regexType);
            ArgumentNullException.ThrowIfNullOrWhiteSpace(regexMethodName);

            _regexType = regexType;
            _regexMethodName = regexMethodName;

            try
            {
                MethodInfo? regexMethod = _regexType.GetMethod(_regexMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (regexMethod is not null && regexMethod.ReturnType.IsAssignableTo(typeof(Regex)))
                {
                    Regex = (Regex?)regexMethod.Invoke(null, null);

                    if (Regex is not null)
                    {
                        Pattern = Regex.ToString() ?? string.Empty;

                        // If the regex does not have a timeout, respect the MatchTimeoutInMilliseconds property instead.
                        // But if the regex has a timeout specified that is within range, use that value.
                        // This can still be overridden by the MatchTimeoutInMilliseconds property if explicitly set.
                        if (Regex.MatchTimeout != Timeout.InfiniteTimeSpan && Regex.MatchTimeout.TotalMilliseconds <= int.MaxValue)
                        {
                            MatchTimeoutInMilliseconds = (int)Regex.MatchTimeout.TotalMilliseconds;
                        }
                    }
                }
            }
            catch
            {
                // Swallow exceptions during construction. The IsValid method will throw if the attribute is ill-formed.
            }
        }

        /// <summary>
        ///     Gets or sets the timeout to use when matching the regular expression pattern (in milliseconds)
        ///     (-1 means never timeout).
        /// </summary>
        public int MatchTimeoutInMilliseconds { get; set; }

        /// <summary>
        /// Gets the timeout to use when matching the regular expression pattern
        /// </summary>
        public TimeSpan MatchTimeout => TimeSpan.FromMilliseconds(MatchTimeoutInMilliseconds);

        /// <summary>
        ///     Gets the regular expression pattern to use
        /// </summary>
        public string Pattern { get; }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.AllMethods)]
        private Type? _regexType;
        private string? _regexMethodName;
        private Regex? Regex { get; set; }

        /// <summary>
        ///     Override of <see cref="ValidationAttribute.IsValid(object)" />
        /// </summary>
        /// <remarks>
        ///     This override performs the specific regular expression matching of the given <paramref name="value" />
        /// </remarks>
        /// <param name="value">The value to test for validity.</param>
        /// <returns><c>true</c> if the given value matches the current regular expression pattern</returns>
        /// <exception cref="InvalidOperationException"> is thrown if the current attribute is ill-formed.</exception>
        /// <exception cref="ArgumentException"> is thrown if the <see cref="Pattern" /> is not a valid regular expression.</exception>
        public override bool IsValid(object? value)
        {
            SetupRegex();

            // Convert the value to a string
            string? stringValue = Convert.ToString(value, CultureInfo.CurrentCulture);

            // Automatically pass if value is null or empty. RequiredAttribute should be used to assert a value is not empty.
            if (string.IsNullOrEmpty(stringValue))
            {
                return true;
            }

            foreach (ValueMatch m in Regex!.EnumerateMatches(stringValue))
            {
                // We are looking for an exact match, not just a search hit. This matches what
                // the RegularExpressionValidator control does
                return m.Index == 0 && m.Length == stringValue.Length;
            }

            return false;
        }

        /// <summary>
        ///     Override of <see cref="ValidationAttribute.FormatErrorMessage" />
        /// </summary>
        /// <remarks>This override provide a formatted error message describing the pattern</remarks>
        /// <param name="name">The user-visible name to include in the formatted message.</param>
        /// <returns>The localized message to present to the user</returns>
        /// <exception cref="InvalidOperationException"> is thrown if the current attribute is ill-formed.</exception>
        /// <exception cref="ArgumentException"> is thrown if the <see cref="Pattern" /> is not a valid regular expression.</exception>
        public override string FormatErrorMessage(string name)
        {
            SetupRegex();

            return string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, Pattern);
        }


        /// <summary>
        ///     Sets up the <see cref="Regex" /> property from the <see cref="Pattern" /> property.
        /// </summary>
        /// <exception cref="ArgumentException"> is thrown if the current <see cref="Pattern" /> cannot be parsed</exception>
        /// <exception cref="InvalidOperationException"> is thrown if the current attribute is ill-formed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"> thrown if <see cref="MatchTimeoutInMilliseconds" /> is negative (except -1),
        /// zero or greater than approximately 24 days </exception>
        private void SetupRegex()
        {
            if (Regex == null)
            {
                if (_regexType is not null && _regexMethodName is not null)
                {
                    MethodInfo regexMethod = _regexType.GetMethod(_regexMethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(SR.RegularExpressionAttribute_RegexMemberError);

                    if (!regexMethod.ReturnType.IsAssignableTo(typeof(Regex)))
                    {
                        throw new InvalidOperationException(SR.RegularExpressionAttribute_RegexMemberError);
                    }

                    try
                    {
                        Regex = regexMethod.Invoke(null, null) as Regex
                            ?? throw new InvalidOperationException(SR.RegularExpressionAttribute_RegexMemberError);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(SR.RegularExpressionAttribute_RegexMemberError, ex.InnerException);
                    }

                    if (string.IsNullOrEmpty(Regex.ToString()))
                    {
                        throw new InvalidOperationException(SR.RegularExpressionAttribute_Empty_Pattern);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(Pattern))
                    {
                        throw new InvalidOperationException(SR.RegularExpressionAttribute_Empty_Pattern);
                    }

                    Regex = MatchTimeoutInMilliseconds == -1
                        ? new Regex(Pattern)
                        : new Regex(Pattern, default(RegexOptions), TimeSpan.FromMilliseconds(MatchTimeoutInMilliseconds));
                }
            }
        }
    }
}
