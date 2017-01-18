﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using i18n.Domain.Abstract;
using i18n.Helpers;

namespace i18n.Domain.Concrete
{
    public class i18nSettings
    {
        private AbstractSettingService _settingService;
        private const string _prefix = "i18n.";
        private const string _allToken = "*";
        private const string _oneToken = "?";

        public i18nSettings(AbstractSettingService settings)
        {
            _settingService = settings;
        }

        public String ProjectDirectory {
            get { return Path.GetDirectoryName(_settingService.GetConfigFileLocation()); }
        }

        private string GetPrefixedString(string key)
        {
            return _prefix + key;
        }

        private string MakePathAbsoluteAndFromConfigFile(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            else
            {
                var startPath = Path.GetDirectoryName(_settingService.GetConfigFileLocation());
                return Path.GetFullPath(Path.Combine(startPath, path));
            }
        }


        /// <summary>
        /// Determines whether the specified path has a windows wildcard character (* or ?)
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>
        ///   <c>true</c> if the specified path has a wildcard otherwise, <c>false</c>.
        /// </returns>
        private static bool HasSearchCharacter(string path)
        {
            return path.Contains(_allToken) || path.Contains(_oneToken);
        }

        /// <summary>
        /// Find all the existing physical paths that corresponds to the specified path.
        /// Returns a single value if there are no wildcards in the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>An enumeration of corresponding paths</returns>
        private IEnumerable<string> FindPaths(string path)
        {
            List<string> paths = new List<string>();
            if (HasSearchCharacter(path))
            {
                string[] parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                paths = GetPaths(parts).ToList();
            }
            else
            {
                paths.Add(path);
            }
            return paths;
        }

        /// <summary>
        /// Recursively gets the path by moving through a directory tree (parts).
        /// </summary>
        /// <param name="parts">The path parts to process.</param>
        /// <param name="root">The root path from where to start.</param>
        /// <returns>A list of existing paths</returns>
        private IEnumerable<string> GetPaths(string[] parts, string root = "")
        {
            if (parts == null || parts.Length == 0)
            {
                if (Directory.Exists(root))
                    return new[] { Path.GetFullPath(root) };
                return Enumerable.Empty<string>();
            }

            List<string> paths = new List<string>();
            if (HasSearchCharacter(parts[0]))
            {
                var rooted = MakePathAbsoluteAndFromConfigFile(root);
                string[] list = Directory.GetDirectories(rooted, parts[0]);
                foreach (string path in list)
                {
                    paths.AddRange(GetPaths(parts.Skip(1).ToArray(), path));
                }
            }
            else
            {
                return GetPaths(parts.Skip(1).ToArray(), Path.Combine(root, parts[0]));
            }

            return paths;
        }

        #region Locale directory

        private const string _localeDirectoryDefault = "locale";
        public virtual string LocaleDirectory
        {
            get
            {
                string prefixedString = GetPrefixedString("LocaleDirectory");
                string setting = _settingService.GetSetting(prefixedString);
                string path;
                if (setting != null)
                {
                    path = setting;    
                }
                else
                {
                    path = _localeDirectoryDefault;
                }

                return MakePathAbsoluteAndFromConfigFile(path);
            }
            set
            {
                string prefixedString = GetPrefixedString("LocaleDirectory");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        #endregion

        #region Locale filename

        private const string _localeFilenameDefault = "messages";
        public virtual string LocaleFilename
        {
            get
            {
                string prefixedString = GetPrefixedString("LocaleFilename");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting.IsSet())
                {
                    return setting;
                }

                return _localeFilenameDefault;
            }
            set
            {
                string prefixedString = GetPrefixedString("LocaleFilename");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string _localeOtherFilesDefault = "";
        public virtual IEnumerable<string> LocaleOtherFiles
        {
            get
            {
                string prefixedString = GetPrefixedString("LocaleOtherFiles");
                string setting = _settingService.GetSetting(prefixedString);
                if (!setting.IsSet())
                {
                    setting = _localeOtherFilesDefault;
                }

                return setting.Split(';');
            }
            set
            {
                string prefixedString = GetPrefixedString("LocaleOtherFiles");
                _settingService.SetSetting(prefixedString, string.Join(";", value));
            }
        }

        #endregion

        #region White list

        private const string _whiteListDefault = "*.cs;*.cshtml";
        
        /// <summary>
        /// Describes zero or more file specifications which in turn specify
        /// the source files to be targeted by FileNuggetParser.
        /// </summary>
        /// <remarks>
        /// Each element in the list may be a full file name e.g. "myfile.js",
        /// or a file extension e.g. "*.js".<br/>
        /// When the list is stored in the config file as a string, each element is delimited by
        /// a semi colon character.<br/>
        /// Defaults to "*.cs;*.cshtml".
        /// </remarks>
        public virtual IEnumerable<string> WhiteList
        {
            get
            {
                string prefixedString = GetPrefixedString("WhiteList");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
                else if (_whiteListDefault.IsSet())
                {
                    return _whiteListDefault.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
                return new List<string>();
            }
            set
            {
                string prefixedString = GetPrefixedString("WhiteList");
                _settingService.SetSetting(prefixedString, string.Join(";", value));
            }
        }

        #endregion

        #region Black list

        private const string _blackListDefault = "";
        private IList<string> _cached_blackList;

        /// <summary>
        /// Describes zero or more source directory/folder paths to be ignored during nugget parsing
        /// e.g. by FileNuggetParser.
        /// </summary>
        /// <remarks>
        /// Each element in the list may be either an absolute (rooted) path, or a path
        /// relative to the folder containing the current config file
        /// (<see cref="AbstractSettingService.GetConfigFileLocation"/>).<br/>
        /// When the list is stored in the config file as a string, each element is delimited by
        /// a semi colon character.<br/>
        /// Default value is empty list.<br/>
        /// </remarks>
        public virtual IEnumerable<string> BlackList
        {
            get
            {
                if(_cached_blackList != null)
                {
                    return _cached_blackList;
                }
                _cached_blackList = new List<string>();
                string prefixedString = GetPrefixedString("BlackList");
                string setting = _settingService.GetSetting(prefixedString);
                //If we find any wildcard in the setting, we replace it by the exitsing physical paths
                if (setting != null && HasSearchCharacter(setting))
                {
                    IEnumerable<string> preblacklist = setting.Split(';');
                    setting = string.Join(";", preblacklist.SelectMany(FindPaths));
                }
                List<string> list;
                if (setting != null)
                {
                    list = setting.Split(';').ToList();
                }
                else if (_blackListDefault.IsSet())
                {
                    list = _blackListDefault.Split(';').ToList();
                }
                else
                {
                    return _cached_blackList;
                }

                foreach (var path in list.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    _cached_blackList.Add(MakePathAbsoluteAndFromConfigFile(path));
                }

                return _cached_blackList;
            }
            set
            {
                string prefixedString = GetPrefixedString("BlackList");
                _settingService.SetSetting(prefixedString, string.Join(";", value));
            }
        }

        #endregion

        #region Nugget tokens

        private const string _nuggetBeginTokenDefault = "[[[";
        public virtual string NuggetBeginToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetBeginToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                else
                {
                    return _nuggetBeginTokenDefault;
                }

            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetBeginToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string _nuggetEndTokenDefault = "]]]";
        public virtual string NuggetEndToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetEndToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                else
                {
                    return _nuggetEndTokenDefault;
                }

            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetEndToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string _nuggetDelimiterTokenDefault = "|||";
        public virtual string NuggetDelimiterToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetDelimiterToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                else
                {
                    return _nuggetDelimiterTokenDefault;
                }

            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetDelimiterToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string _nuggetCommentTokenDefault = "///";
        public virtual string NuggetCommentToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetCommentToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                else
                {
                    return _nuggetCommentTokenDefault;
                }

            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetCommentToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string NuggetParameterBeginTokenDefault = "(((";
        public virtual string NuggetParameterBeginToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetParameterBeginToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                return NuggetParameterBeginTokenDefault;
            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetParameterBeginToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string NuggetParameterEndTokenDefault = ")))";
        public virtual string NuggetParameterEndToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetParameterEndToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                return NuggetParameterEndTokenDefault;
            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetParameterEndToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        private const string NuggetVisualizeTokenDefault = "!";
        public virtual string NuggetVisualizeToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetVisualizeToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                return NuggetVisualizeTokenDefault;
            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetVisualizeToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        public virtual string NuggetVisualizeEndToken
        {
            get
            {
                string prefixedString = GetPrefixedString("NuggetVisualizeEndToken");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                return string.Empty;
            }
            set
            {
                string prefixedString = GetPrefixedString("NuggetVisualizeEndToken");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        #endregion
        
        #region DirectoriesToScan

        private const string _directoriesToScan = ".";

        /// <summary>
        /// A semi-colon-delimited string that specifies one or more paths to the 
        /// root directory/folder of the branches which FileNuggetParser is to scan for source files.
        /// </summary>
        /// <remarks>
        /// Each string may be an absolute (rooted) path, or a path
        /// relative to the folder containing the current config file
        /// (<see cref="AbstractSettingService.GetConfigFileLocation"/>).<br/>
        /// Default value is "." which equates to the the single folder containing the 
        /// current config file (<see cref="AbstractSettingService.GetConfigFileLocation"/>).<br/>
        /// Typically, you may set to ".." equating to the solution folder for the
        /// project containing the current config file.<br/>
        /// An example of a multi-path string is "c:\mywebsite;c:\mylibs\asp.net".
        /// </remarks>
        public virtual IEnumerable<string> DirectoriesToScan
        {
            get
            {
                string prefixedString = GetPrefixedString("DirectoriesToScan");
                string setting = _settingService.GetSetting(prefixedString);
                List<string> list;
                if (setting != null)
                {
                    list = setting.Split(';').ToList();
                }
                else
                {
                    list = _directoriesToScan.Split(';').ToList();
                }

                List<string> returnList = new List<string>();
                foreach (var path in list.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    returnList.Add(MakePathAbsoluteAndFromConfigFile(path));
                }

                return returnList;
            }
            set
            {
                string prefixedString = GetPrefixedString("DirectoriesToScan");
                _settingService.SetSetting(prefixedString, string.Join(";", value));
            }
        }

        #endregion
        
        #region Available Languages

        //If empty string is returned the repository can if it choses enumerate languages in a different way (like enumerating directories in the case of PO files)
        //empty string is returned as an IEnumerable with one empty element
        private const string _availableLanguages = "";
        public virtual IEnumerable<string> AvailableLanguages
        {
            get
            {
                string prefixedString = GetPrefixedString("AvailableLanguages");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
                else
                {
                    return _availableLanguages.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
            }
            set
            {
                string prefixedString = GetPrefixedString("AvailableLanguages");
                _settingService.SetSetting(prefixedString, string.Join(";", value));
            }
        }

        #endregion

        #region MessageContextEnabledFromComment

        private bool? _cached_MessageContextEnabledFromComment;
        public virtual bool MessageContextEnabledFromComment
        {
            get
            {
                // NB: this is not particularly thread-safe, but not seen as dangerous
                // if done concurrently as modification is one-way.
                if (_cached_MessageContextEnabledFromComment != null) {
                    return _cached_MessageContextEnabledFromComment.Value; }

                string prefixedString = GetPrefixedString("MessageContextEnabledFromComment");
                string setting = _settingService.GetSetting(prefixedString);
                bool result = !string.IsNullOrEmpty(setting) &&  setting == "true";
                _cached_MessageContextEnabledFromComment = result;
                return result;
            }
            set
            {
                string prefixedString = GetPrefixedString("MessageContextEnabledFromComment");
                _settingService.SetSetting(prefixedString, value ? "true" : "false");
                _cached_MessageContextEnabledFromComment = value;
            }
        }

        #endregion

        #region VisualizeMessages

        private bool? _cached_visualizeMessages;
        public virtual bool VisualizeMessages
        {
            get
            {
                // NB: this is not particularly thread-safe, but not seen as dangerous
                // if done concurrently as modification is one-way.
                if (_cached_visualizeMessages != null)
                {
                    return _cached_visualizeMessages.Value;
                }

                string prefixedString = GetPrefixedString("VisualizeMessages");
                string setting = _settingService.GetSetting(prefixedString);
                bool result = !string.IsNullOrEmpty(setting) && setting == "true";
                _cached_visualizeMessages = result;
                return _cached_visualizeMessages.Value;
            }
            set
            {
                string prefixedString = GetPrefixedString("VisualizeMessages");
                _settingService.SetSetting(prefixedString, value ? "true" : "false");
                _cached_visualizeMessages = value;
            }
        }

        public virtual string VisualizeLanguageSeparator
        {
            get
            {
                string prefixedString = GetPrefixedString("VisualizeLanguageSeparator");
                string setting = _settingService.GetSetting(prefixedString);
                if (setting != null)
                {
                    return setting;
                }
                return string.Empty;
            }
            set
            {
                string prefixedString = GetPrefixedString("VisualizeLanguageSeparator");
                _settingService.SetSetting(prefixedString, value);
            }
        }

        #endregion

        #region DisableReferences
        private bool? _cached_disableReferences;

        public virtual bool DisableReferences
        {
            get
            {
                if (_cached_disableReferences != null)
                {
                    return _cached_disableReferences.Value;
                }

                string prefixedString = GetPrefixedString("DisableReferences");
                string setting = _settingService.GetSetting(prefixedString);
                bool result = !string.IsNullOrEmpty(setting) && setting == "true";
                _cached_disableReferences = result;
                return _cached_disableReferences.Value;
            }
            set
            {
                string prefixedString = GetPrefixedString("DisableReferences");
                _settingService.SetSetting(prefixedString, value ? "true" : "false");
                _cached_disableReferences = value;
            }
        }
        #endregion

        #region GenerateTemplatePerFile

        private bool? _cached_generateTemplatePerFile;
        public virtual bool GenerateTemplatePerFile
        {
            get
            {
                // NB: this is not particularly thread-safe, but not seen as dangerous
                // if done concurrently as modification is one-way.
                if (_cached_generateTemplatePerFile != null)
                {
                    return _cached_generateTemplatePerFile.Value;
                }

                string prefixedString = GetPrefixedString("GenerateTemplatePerFile");
                string setting = _settingService.GetSetting(prefixedString);
                bool result = !string.IsNullOrEmpty(setting) && setting == "true";
                _cached_generateTemplatePerFile = result;
                return _cached_generateTemplatePerFile.Value;
            }
            set
            {
                string prefixedString = GetPrefixedString("GenerateTemplatePerFile");
                _settingService.SetSetting(prefixedString, value ? "true" : "false");
                _cached_generateTemplatePerFile = value;
            }
        }

        #endregion
    }
}
