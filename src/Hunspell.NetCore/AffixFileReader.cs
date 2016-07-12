﻿using Hunspell.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hunspell
{
    internal class AffixFileReader
    {
        public AffixFileReader(IAffixFileLineReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            FlagMode = FlagMode.Char;
            Reader = reader;
        }

        private static readonly Regex LineStringParseRegex = new Regex(@"^[ \t]*(\w+)[ \t]+(.+)[ \t]*$");
        private static readonly Regex SingleCommandParseRegex = new Regex(@"^[ \t]*(\w+)[ \t]*$");
        private static readonly Regex CommentLineRegex = new Regex(@"^\s*[#].*");
        private static readonly Regex AffixLineRegex = new Regex(
            @"^[\t ]*([^\t ]+)[\t ]+(?:([^\t ]+)[\t ]+([^\t ]+)|([^\t ]+)[\t ]+([^\t ]+)[\t ]+([^\t ]+)(?:[\t ]+(.+))?)[\t ]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public FlagMode FlagMode { get; private set; }

        public IAffixFileLineReader Reader { get; }

        private bool hasInitializedReplacements = false;

        private bool hasInitializedCompoundRules = false;

        public async Task<AffixFile> ReadAsync()
        {
            var result = new AffixFile();

            string line;
            while (null != (line = await Reader.ReadLineAsync()))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (CommentLineRegex.IsMatch(line))
                {
                    continue;
                }

                var singleCommandParsed = SingleCommandParseRegex.Match(line);
                if (
                    singleCommandParsed.Success
                    && result.TrySetOption(singleCommandParsed.Groups[1].Value, true))
                {
                    continue;
                }

                var multiPartCommandParsed = LineStringParseRegex.Match(line);
                if (
                    multiPartCommandParsed.Success
                    && TryHandleTwoPartCommand(result, multiPartCommandParsed.Groups[1].Value, multiPartCommandParsed.Groups[2].Value))
                {
                    continue;
                }
            }

            return result;
        }
        

        private bool TryHandleTwoPartCommand(AffixFile affixFile, string name, string parameters)
        {
            if (name == null || parameters == null)
            {
                return false;
            }

            name = name.Trim();

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            switch (CultureInfo.InvariantCulture.TextInfo.ToUpper(name))
            {
                case "KEY": // parse in the keyboard string
                    affixFile.KeyString = parameters;
                    return true;
                case "TRY": // parse in the try string
                    affixFile.TryString = parameters;
                    return true;
                case "SET": // parse in the name of the character set used by the .dict and .aff
                    affixFile.RequestedEncoding = parameters;
                    return true;
                case "LANG": // parse in the language for language specific codes
                    affixFile.Language = parameters;
                    return true;
                case "SYLLABLENUM": // parse in the flag used by compound_check() method
                    affixFile.CompoundSyllableNum = parameters;
                    return true;
                case "WORDCHARS": // parse in the extra word characters
                    affixFile.WordChars = parameters;
                    return true;
                case "IGNORE": // parse in the ignored characters (for example, Arabic optional diacretics characters)
                    affixFile.IgnoredChars = parameters;
                    return true;
                case "COMPOUNDFLAG": // parse in the flag used by the controlled compound words
                    return TryParseFlag(parameters, out affixFile.compoundFlag);
                case "COMPOUNDMIDDLE": // parse in the flag used by compound words
                    return TryParseFlag(parameters, out affixFile.compoundMiddle);
                case "COMPOUNDBEGIN": // parse in the flag used by compound words
                    return affixFile.ComplexPrefixes
                        ? TryParseFlag(parameters, out affixFile.compoundEnd)
                        : TryParseFlag(parameters, out affixFile.compoundBegin);
                case "COMPOUNDEND": // parse in the flag used by compound words
                    return affixFile.ComplexPrefixes
                        ? TryParseFlag(parameters, out affixFile.compoundBegin)
                        : TryParseFlag(parameters, out affixFile.compoundEnd);
                case "COMPOUNDWORDMAX": // parse in the data used by compound_check() method
                    return IntExtensions.TryParseInvariant(parameters, out affixFile.compoundWordMax);
                case "COMPOUNDMIN": // parse in the minimal length for words in compounds
                    return IntExtensions.TryParseInvariant(parameters, out affixFile.compoundMin);
                case "COMPOUNDROOT": // parse in the flag sign compounds in dictionary
                    return TryParseFlag(parameters, out affixFile.compoundRoot);
                case "COMPOUNDFORBIDFLAG": // parse in the flag used by compound_check() method
                    return TryParseFlag(parameters, out affixFile.compoundForbidFlag);
                case "COMPOUNDSYLLABLE": // parse in the max. words and syllables in compounds
                    throw new NotImplementedException();
                case "NOSUGGEST":
                    return TryParseFlag(parameters, out affixFile.noSuggest);
                case "NONGRAMSUGGEST":
                    return TryParseFlag(parameters, out affixFile.noNgramSuggest);
                case "FORBIDDENWORD": // parse in the flag used by forbidden words
                    return TryParseFlag(parameters, out affixFile.forbiddenWord);
                case "LEMMA_PRESENT": // parse in the flag used by forbidden words
                    return TryParseFlag(parameters, out affixFile.lemmaPresent);
                case "CIRCUMFIX": // parse in the flag used by circumfixes
                    return TryParseFlag(parameters, out affixFile.circumfix);
                case "ONLYINCOMPOUND": // parse in the flag used by fogemorphemes
                    return TryParseFlag(parameters, out affixFile.onlyInCompound);
                case "PSEUDOROOT": // parse in the flag used by `needaffixs'
                case "NEEDAFFIX": // parse in the flag used by `needaffixs'
                    return TryParseFlag(parameters, out affixFile.needAffix);
                case "REP": // parse in the typical fault correcting table
                    return TryParseReplacementEntryLineIntoReplacements(affixFile, parameters);
                case "ICONV": // parse in the input conversion table
                    throw new NotImplementedException();
                case "OCONV": // parse in the input conversion table
                    throw new NotImplementedException();
                case "PHONE": // parse in the input conversion table
                    throw new NotImplementedException();
                case "CHECKCOMPOUNDPATTERN": // parse in the checkcompoundpattern table
                    throw new NotImplementedException();
                case "COMPOUNDRULE": // parse in the defcompound table
                    return TryParseCompoundRuleIntoList(affixFile, parameters);
                case "MAP": // parse in the related character map table
                    throw new NotImplementedException();
                case "BREAK": // parse in the word breakpoints table
                    throw new NotImplementedException();
                case "VERSION":
                    throw new NotImplementedException();
                case "MAXNGRAMSUGS":
                    return IntExtensions.TryParseInvariant(parameters, out affixFile.maxNgramSuggestions);
                case "MAXDIFF":
                    return IntExtensions.TryParseInvariant(parameters, out affixFile.maxDifferency);
                case "MAXCPDSUGS":
                    return IntExtensions.TryParseInvariant(parameters, out affixFile.maxCompoundSuggestions);
                case "KEEPCASE": // parse in the flag used by forbidden words
                    return TryParseFlag(parameters, out affixFile.keepCase);
                case "FORCEUCASE":
                    return TryParseFlag(parameters, out affixFile.forceUpperCase);
                case "WARN":
                    throw new NotImplementedException();
                case "SUBSTANDARD":
                    throw new NotImplementedException();
                case "PFX":
                    return TryParseAffixIntoList(affixFile, parameters, affixFile.Prefixes);
                case "SFX":
                    return TryParseAffixIntoList(affixFile, parameters, affixFile.Suffixes);
                default:
                    return false;
            }
        }


        private bool TryParseCompoundRuleIntoList(AffixFile affixFile, string parameterText)
        {
            if (string.IsNullOrEmpty(parameterText))
            {
                return false;
            }

            if (!hasInitializedCompoundRules || affixFile.CompoundRules == null)
            {
                int expectedSize;
                if(IntExtensions.TryParseInvariant(parameterText, out expectedSize) && expectedSize > 0)
                {
                    affixFile.CompoundRules = new List<CompoundRule>(expectedSize);
                    return true;
                }
                else
                {
                    affixFile.CompoundRules = new List<CompoundRule>();
                }

                hasInitializedCompoundRules = true;
            }

            throw new NotImplementedException();

            return true;
        }

        private bool TryParseAffixIntoList<TEntry>(AffixFile affixFile, string parameterText, List<AffixEntryGroup<TEntry>> groups)
            where TEntry : AffixEntry, new()
        {
            var lineMatch = AffixLineRegex.Match(parameterText);
            if (!lineMatch.Success)
            {
                return false;
            }

            var lineMatchGroups = lineMatch.Groups;

            var characterFlag = lineMatchGroups[1].Value[0];
            var affixGroup = groups.FindLast(g => g.AFlag == characterFlag);

            if (lineMatchGroups[2].Success && lineMatchGroups[3].Success)
            {
                if (affixGroup != null)
                {
                    return false;
                }

                var options = AffixEntryOptions.None;
                if (lineMatchGroups[2].Value.StartsWith('Y'))
                {
                    options |= AffixEntryOptions.CrossProduct;
                }
                if (affixFile.IsAliasM)
                {
                    options |= AffixEntryOptions.AliasM;
                }
                if (affixFile.IsAliasF)
                {
                    options |= AffixEntryOptions.AliasF;
                }

                int expectedEntryCount;
                IntExtensions.TryParseInvariant(lineMatchGroups[3].Value, out expectedEntryCount);

                affixGroup = new AffixEntryGroup<TEntry>(characterFlag, options, expectedEntryCount);
                groups.Add(affixGroup);

                return true;
            }
            else if (lineMatchGroups[4].Success && lineMatchGroups[5].Success && lineMatchGroups[6].Success)
            {
                var strip = lineMatchGroups[4].Value;
                if (strip == "0")
                {
                    strip = string.Empty;
                }
                if (affixFile.ComplexPrefixes)
                {
                    strip = strip.Reverse();
                }

                var affixText = lineMatchGroups[5].Value;
                var affixSlashIndex = affixText.IndexOf('/');
                if (affixSlashIndex >= 0)
                {
                    var slashPart = affixText.Substring(affixSlashIndex + 1);
                    affixText = affixText.Substring(0, affixSlashIndex);
                }
                if (affixFile.IgnoredChars != null)
                {
                    affixText = affixText.RemoveChars(affixFile.IgnoredChars);
                }
                if (affixFile.ComplexPrefixes)
                {
                    affixText = affixText.Reverse();
                }
                if (affixText == "0")
                {
                    affixText = string.Empty;
                }

                var conditionsText = lineMatchGroups[6].Value;
                if (affixFile.ComplexPrefixes)
                {
                    conditionsText = conditionsText.Reverse();
                    throw new NotImplementedException("reverse_condition");
                }
                if (!string.IsNullOrEmpty(strip) && conditionsText != "." && RedundantCondition(strip, conditionsText))
                {
                    conditionsText = ".";
                }
                if (typeof(TEntry) == typeof(SuffixEntry))
                {
                    // TODO: reverse some stuff or do it in SuffixEntry somehow, or dont at all!
                }

                string morph = null;
                if (lineMatchGroups[7].Success)
                {
                    morph = lineMatchGroups[7].Value;
                    if (affixFile.IsAliasM)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        if (affixFile.ComplexPrefixes)
                        {
                            morph = morph.Reverse();
                        }
                    }
                }

                if (affixGroup == null)
                {
                    affixGroup = new AffixEntryGroup<TEntry>(characterFlag, AffixEntryOptions.None, 0);
                }

                affixGroup.Entries.Add(new TEntry
                {
                    Strip = strip,
                    Append = affixText,
                    Conditions = conditionsText,
                    MorphCode = morph
                });

                return true;
            }
            else
            {
                return false;
            }
        }

        private bool RedundantCondition(string strip, string condition)
        {
            throw new NotImplementedException();
        }

        private bool TryParseReplacementEntryLineIntoReplacements(AffixFile affixFile, string parameterText)
        {
            var parameters = parameterText.SplitOnTabOrSpace();
            if (parameters.Length == 0)
            {
                return false;
            }
            
            if (parameters.Length == 1)
            {
                if (!hasInitializedReplacements)
                {
                    int expectedCount;
                    if (IntExtensions.TryParseInvariant(parameters[0], out expectedCount) && expectedCount >= 0)
                    {
                        affixFile.Replacements = new List<ReplacementEntry>(Math.Max(1, expectedCount));
                        hasInitializedReplacements = true;
                        return true;
                    }
                }

                return false;
            }

            var pattern = parameters[0].Replace('_', ' ');
            var outString = parameters[1].Replace('_', ' ');
            int type = 0;

            if (pattern.StartsWith('^'))
            {
                pattern = pattern.Substring(1);
                type = 1;
            }

            if (pattern.EndsWith('$'))
            {
                pattern = pattern.SubstringFromEnd(1);
                type |= 2;
            }

            if (affixFile.Replacements == null)
            {
                affixFile.Replacements = new List<ReplacementEntry>();
                hasInitializedReplacements = true;
            }

            var replacement = new ReplacementEntry(pattern);
            replacement.OutStrings[type] = outString;
            affixFile.Replacements.Add(replacement);

            return true;
        }

        private bool TryParseFlag(string text, out int result)
        {
            if (!string.IsNullOrEmpty(text))
            {
                switch (FlagMode)
                {
                    case FlagMode.Char:
                        if (text.Length >= 1)
                        {
                            result = text[0];
                            return true;
                        }

                        break;
                    case FlagMode.Long:
                        if (text.Length >= 2)
                        {
                            result = (text[0] << 8) | (byte)(text[1]);
                            return true;
                        }
                        if (text.Length == 1)
                        {
                            result = text[1];
                            return true;
                        }

                        break;
                    case FlagMode.Num:
                        throw new NotImplementedException();
                    case FlagMode.Uni:
                        throw new NotImplementedException();
                }
            }

            result = 0;
            return false;
        }
    }
}