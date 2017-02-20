﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Caching;
using i18n.Domain.Abstract;
using i18n.Domain.Entities;
using i18n.Helpers;

namespace i18n.Domain.Concrete
{
    public class POTranslationRepository : ITranslationRepository
    {
        private readonly i18nSettings _settings;

        public POTranslationRepository(i18nSettings settings)
        {
            _settings = settings;
        }

        #region load and getters

        public Translation GetTranslation(string langtag, List<string> fileNames = null, bool loadingCache = true)
        {
            return ParseTranslationFile(langtag, fileNames, loadingCache);
        }


        /// <summary>
        /// Checks in first hand settings file, if not found there it checks file structure
        /// </summary>
        /// <returns>List of available languages</returns>
        public IEnumerable<Language> GetAvailableLanguages()
        {
            //todo: ideally we want to fill the other data in the Language object so this is usable by project incorporating i18n that they can simply lookup available languages. Maybe we even add a country property so that it's easier for projects to add corresponding flags.

            List<string> languages = _settings.AvailableLanguages.ToList();
            Language lang;
            List<Language> dirList = new List<Language>();


            //This means there was no languages from settings
            if (languages.Count == 0
                || (languages.Count == 1 && languages[0] == ""))
            {
                //We instead check for file structure
                DirectoryInfo di = new DirectoryInfo(GetAbsoluteLocaleDir());

                foreach (var dir in di.EnumerateDirectories().Select(x => x.Name))
                {
                    try
                    {
                        var lt = new LanguageTag(dir);
                        if (lt.CultureInfo == null)
                            throw new CultureNotFoundException(dir);
                        lang = new Language
                        {
                            LanguageShortTag = dir
                        };
                        dirList.Add(lang);
                    }
                    catch (System.Globalization.CultureNotFoundException)
                    {
                        //There is a directory in the locale directory that is not a valid culture so ignore it
                    }
                }
            }
            else
            {
                //see if the desired language was one of the returned from settings
                foreach (var language in languages)
                {
                    lang = new Language();
                    lang.LanguageShortTag = language;
                    dirList.Add(lang);
                }
            }

            return dirList;
        }

        /// <summary>
        /// Checks if the language is set as supported in config file
        /// If not it checks if the PO file is available
        /// </summary>
        /// <param name="langtag">The tag for which you want to check if support exists. For instance "sv-SE"</param>
        /// <returns>True if language exists, otherwise false</returns>
        public bool TranslationExists(string langtag)
        {
            List<string> languages = _settings.AvailableLanguages.ToList();

            //This means there was no languages from settings
            if (languages.Count == 0
                || (languages.Count == 1 && languages[0] == ""))
            {
                //We instead check if the file exists
                return File.Exists(GetPathForLanguage(langtag));
            }
            else
            {
                //see if the desired language was one of the returned from settings
                foreach (var language in languages)
                {
                    if (language == langtag)
                    {
                        return true;
                    }
                }
            }

            //did not exist in settings nor as file, we return false
            return false;
        }

        public CacheDependency GetCacheDependencyForSingleLanguage(string langtag)
        {
            var path = GetPathForLanguage(langtag);
            if (!File.Exists(path))
            {
                return null;
            }
            return new CacheDependency(path);
        }

        public CacheDependency GetCacheDependencyForAllLanguages()
        {
            return new FsCacheDependency(GetAbsoluteLocaleDir());
        }

        #endregion

        #region save

        /// <summary>
        /// Saves a translation into file with standard pattern locale/langtag/message.po
        /// Also saves a backup of previous version
        /// </summary>
        /// <param name="translation">The translation you wish to save. Must have Language shortag filled out.</param>
        public void SaveTranslation(Translation translation, List<string> fileNamePaths)
        {
            var fileNames = new List<string>(fileNamePaths)
            {
                GetPathForLanguage(translation.LanguageInformation.LanguageShortTag)
            };

            //var templateFilePath = GetAbsoluteLocaleDir() + "/" + _settings.LocaleFilename + ".pot";
            var potDate = $"\"POT-Creation-Date: {DateTime.Now:yyyy-MM-dd HH:mmzzz}\\n\"";

            //if (File.Exists(templateFilePath))
            //{
            //    potDate = File.ReadAllLines(templateFilePath).Skip(3).FirstOrDefault();
            //}

            fileNamePaths.Add(_settings.LocaleFilename);

            for (var y = 0; y < fileNamePaths.Count; y++)
            {
                fileNamePaths[y] = GetPathForLanguage(translation.LanguageInformation.LanguageShortTag,
                    fileNamePaths[y]);
                CreateFileIfNotExists(fileNamePaths[y]);

                var templateFilePath = GetAbsoluteLocaleDir() + "/" + fileNames[y] + ".pot";
                if (File.Exists(templateFilePath))
                {
                    potDate = File.ReadAllLines(templateFilePath).Skip(3).FirstOrDefault();
                }

                var fileNamePotList = new List<string>(fileNamePaths)
                {
                    [y] = GetPathForLanguage(translation.LanguageInformation.LanguageShortTag, fileNames[y]) +
                          ".backup"
                };

                var tempFile = $"{fileNamePaths[y]}.temp";

                using (var stream = new StreamWriter(tempFile))
                {
                    DebugHelpers.WriteLine("Writing file: {0}", tempFile);

                    IEnumerable<TranslationItem> orderedItems = translation.Items.Values;

                    if (_settings.GenerateTemplatePerFile)
                    {
                        orderedItems = translation.Items.Values
                            .OrderBy(x => x.References == null || !x.References.Any())
                            .ThenBy(x => x.MsgKey)
                            .Where(x => x.FileName == fileNames[y]);
                    }

                    if (fileNamePaths[y] ==
                        GetPathForLanguage(translation.LanguageInformation.LanguageShortTag,
                            _settings.LocaleFilename))
                    {
                        orderedItems = translation.Items.Values
                            .OrderBy(x => x.References == null || !x.References.Any())
                            .ThenBy(x => x.MsgKey);
                    }

                    //This is required for poedit to read the files correctly if they contains for instance swedish characters
                    OutputHeader(stream, potDate);

                    foreach (var item in orderedItems)
                    {
                        var hasReferences = false;

                        if (item.TranslatorComments != null)
                        {
                            foreach (var translatorComment in item.TranslatorComments.Distinct())
                            {
                                stream.WriteLine("# " + translatorComment);
                            }
                        }

                        if (item.ExtractedComments != null)
                        {
                            foreach (var extractedComment in item.ExtractedComments.Distinct())
                            {
                                stream.WriteLine("#. " + extractedComment);
                            }
                        }

                        if (item.References != null)
                        {
                            foreach (var reference in item.References.Distinct())
                            {
                                hasReferences = true;
                                stream.WriteLine("#: " + reference.ToComment());
                            }
                        }

                        if (item.Flags != null)
                        {
                            foreach (var flag in item.Flags.Distinct())
                            {
                                stream.WriteLine("#, " + flag);
                            }
                        }

                        string prefix = hasReferences ? "" : prefix = "#~ ";

                        if (_settings.MessageContextEnabledFromComment
                            && item.ExtractedComments != null
                            && item.ExtractedComments.Count() != 0)
                        {
                            WriteString(stream, hasReferences, "msgctxt", item.ExtractedComments.First());
                        }

                        WriteString(stream, hasReferences, "msgid", escape(item.MsgId));
                        WriteString(stream, hasReferences, "msgstr", escape(item.Message));

                        stream.WriteLine("");
                    }
                }

                if (FilesTheSame(tempFile, fileNamePaths[y], 5))
                {
                    //if (File.Exists(fileNamePaths[y]))
                    //    File.Copy(fileNamePotList[y], fileNamePaths[y], true);
                    //else
                    //    File.Move(fileNamePotList[y], fileNamePaths[y]);

                    File.Delete(tempFile);
                }
                else
                {
                    CreateBackupFile(fileNamePaths[y]);
                    File.Move(tempFile, fileNamePaths[y]);
                }
            }
        }

        /// <summary>
        /// Saves a template file which is a all strings (needing translation) used in the entire project. Not language dependent
        /// </summary>
        /// <param name="items">A list of template items to save. The list should be all template items for the entire project.</param>
        public bool SaveTemplate(IDictionary<string, TemplateItem> items)
        {
            if (_settings.GenerateTemplatePerFile)
            {
                bool result = false;
                foreach (var item in items.GroupBy(x => x.Value.FileName))
                {
                    result |= SaveTemplate(item.ToDictionary(x => x.Key, x => x.Value), item.Key);
                }
                return result;
            }
            else
            {
                return SaveTemplate(items, string.Empty);
            }
        }

        private bool SaveTemplate(IDictionary<string, TemplateItem> items, string fileName)
        {
            var filePath = GetAbsoluteLocaleDir() + "/" +
                              (!string.IsNullOrWhiteSpace(fileName) ? fileName : _settings.LocaleFilename) + ".pot";
            var backupPath = filePath + ".backup";
            var tempPath = $"{filePath}.temp";

            CreateFileIfNotExists(filePath);

            using (StreamWriter stream = new StreamWriter(tempPath))
            {
                DebugHelpers.WriteLine("Writing file: {0}", tempPath);
                // Establish ordering of items in PO file.
                var orderedItems = items.Values
                    .OrderBy(x => x.References == null || !x.References.Any())
                    // Non-orphan items before orphan items.
                    .ThenBy(x => x.MsgKey);
                // Then order alphanumerically.

                // This is required for poedit to read the files correctly if they contains 
                // for instance swedish characters.
                OutputHeader(stream);

                foreach (var item in orderedItems)
                {
                    if (item.Comments != null)
                    {
                        foreach (var comment in item.Comments)
                        {
                            stream.WriteLine("#. " + comment);
                        }
                    }

                    foreach (var reference in item.References)
                    {
                        stream.WriteLine("#: " + reference.ToComment());
                    }

                    if (_settings.MessageContextEnabledFromComment
                        && item.Comments != null
                        && item.Comments.Count() != 0)
                    {
                        WriteString(stream, true, "msgctxt", item.Comments.First());
                    }

                    WriteString(stream, true, "msgid", escape(item.MsgId));
                    WriteString(stream, true, "msgstr", ""); // enable loading of POT file into editor e.g. PoEdit.

                    stream.WriteLine("");
                }
            }

            if (!FilesTheSame(tempPath, filePath))
            {
                CreateBackupFile(filePath);
                File.Move(tempPath, filePath);
            }
            else
            {
                File.Delete(tempPath);
            }

            return true;
        }

        #endregion

        #region helpers

        /// <summary>
        /// Gets the locale directory from settings and makes sure it is translated into absolut path
        /// </summary>
        /// <returns>the locale directory in absolute path</returns>
        private string GetAbsoluteLocaleDir()
        {
            return _settings.LocaleDirectory;
        }

        private string GetPathForLanguage(string langtag, string filename = null)
        {
            if (!filename.IsSet())
                filename = _settings.LocaleFilename;
            return Path.Combine(GetAbsoluteLocaleDir(), langtag, filename + ".po");
        }

        /// <summary>
        /// Parses a PO file into a Language object
        /// </summary>
        /// <param name="langtag">The language (tag) you wish to load into Translation object</param>
        /// <returns>A complete translation object with all all translations and language values set.</returns>
        private Translation ParseTranslationFile(string langtag, List<string> fileNames, bool loadingCache)
        {
            //todo: consider that lines we don't understand like headers from poedit and #| should be preserved and outputted again.

            Translation translation = new Translation();
            Language language = new Language();
            language.LanguageShortTag = langtag;
            translation.LanguageInformation = language;
            var items = new ConcurrentDictionary<string, TranslationItem>();

            List<string> paths = new List<string>();

            if (!_settings.GenerateTemplatePerFile || loadingCache)
            {
                paths.Add(GetPathForLanguage(langtag));
            }

            foreach (var file in _settings.LocaleOtherFiles)
            {
                if (file.IsSet())
                {
                    paths.Add(GetPathForLanguage(langtag, file));
                }
            }

            if (_settings.GenerateTemplatePerFile && !loadingCache)
            {
                if (fileNames != null && fileNames.Count > 0)
                {
                    foreach (var fileName in fileNames)
                    {
                        paths.Add(GetPathForLanguage(langtag, fileName));
                    }
                }
            }

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    DebugHelpers.WriteLine("Reading file: {0}", path);

                    using (var fs = File.OpenText(path))
                    {
                        // http://www.gnu.org/s/hello/manual/gettext/PO-Files.html

                        string line;
                        bool itemStarted = false;
                        while ((line = fs.ReadLine()) != null)
                        {
                            var extractedComments = new HashSet<string>();
                            var translatorComments = new HashSet<string>();
                            var flags = new HashSet<string>();
                            var references = new List<ReferenceContext>();

                            //read all comments, flags and other descriptive items for this string
                            //if we have #~ its a historical/log entry but it is the messageID/message so we skip this do/while
                            if (line.StartsWith("#") && !line.StartsWith("#~"))
                            {
                                do
                                {
                                    itemStarted = true;
                                    switch (line[1])
                                    {
                                        case '.': //Extracted comments
                                            extractedComments.Add(line.Substring(2).Trim());
                                            break;
                                        case ':': //references
                                            references.Add(ReferenceContext.Parse(line.Substring(2).Trim()));
                                            break;
                                        case ',': //flags
                                            flags.Add(line.Substring(2).Trim());
                                            break;
                                        case '|': //msgid previous-untranslated-string - NOT used by us
                                            break;
                                        default: //translator comments
                                            translatorComments.Add(line.Substring(1).Trim());
                                            break;
                                    }
                                } while ((line = fs.ReadLine()) != null && line.StartsWith("#"));
                            }

                            if (line != null && (itemStarted || line.StartsWith("#~")))
                            {
                                TranslationItem item = ParseBody(fs, line, extractedComments);
                                if (item != null)
                                {
                                    //
                                    item.TranslatorComments = translatorComments;
                                    item.ExtractedComments = extractedComments;
                                    item.Flags = flags;
                                    item.References = references;
                                    //
                                    items.AddOrUpdate(
                                        item.MsgKey,
                                        // Add routine.
                                        k => { return item; },
                                        // Update routine.
                                        (k, v) =>
                                        {
                                            v.References = v.References.Append(item.References);
                                            var referencesAsComments =
                                                item.References.Select(r => r.ToComment()).ToList();
                                            v.ExtractedComments = v.ExtractedComments.Append(referencesAsComments);
                                            v.TranslatorComments = v.TranslatorComments.Append(referencesAsComments);
                                            v.Flags = v.Flags.Append(referencesAsComments);
                                            return v;
                                        });
                                }
                            }

                            itemStarted = false;
                        }
                    }
                }
            }
            translation.Items = items;
            return translation;
        }

        /// <summary>
        /// Removes the preceding characters in a file showing that an item is historical/log. That is to say it has been removed from the project. We don't need care about the character as the fact that it lacks references is what tells us it's a log item
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private string RemoveCommentIfHistorical(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                //return null;
                return line;
            }

            if (line.StartsWith("#~"))
            {
                return line.Replace("#~", "").Trim();
            }

            return line;
        }

        /// <summary>
        /// Parses the body of a PO file item. That is to say the message id and the message itself.
        /// Reason for why it must be on second line (textreader) is so that you can read until you have read to far without peek previously for meta data.
        /// </summary>
        /// <param name="fs">A textreader that must be on the second line of a message body</param>
        /// <param name="line">The first line of the message body.</param>
        /// <returns>Returns a TranslationItem with only key, id and message set</returns>
        private TranslationItem ParseBody(TextReader fs, string line, IEnumerable<string> extractedComments)
        {
            string originalLine = line;

            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            TranslationItem message = new TranslationItem {MsgKey = ""};
            StringBuilder sb = new StringBuilder();

            string msgctxt = null;
            line = RemoveCommentIfHistorical(line); //so that we read in removed historical records too
            if (line.StartsWith("msgctxt"))
            {
                msgctxt = Unquote(line);
                line = fs.ReadLine();
            }

            line = RemoveCommentIfHistorical(line); //so that we read in removed historical records too
            if (line.StartsWith("msgid"))
            {
                var msgid = Unquote(line);
                sb.Append(msgid);

                while ((line = fs.ReadLine()) != null)
                {
                    line = RemoveCommentIfHistorical(line);
                    if (String.IsNullOrEmpty(line))
                    {
                        DebugHelpers.WriteLine("ERROR - line is empty. Original line: " + originalLine);
                        continue;
                    }
                    if (!line.StartsWith("msgstr") && (msgid = Unquote(line)) != null)
                    {
                        sb.Append(msgid);
                    }
                    else
                    {
                        break;
                    }
                }

                message.MsgId = Unescape(sb.ToString());

                // If no msgctxt is set then msgkey is the msgid; otherwise it is msgid+msgctxt.
                message.MsgKey = string.IsNullOrEmpty(msgctxt)
                    ? message.MsgId
                    : TemplateItem.KeyFromMsgidAndComment(message.MsgId, msgctxt, true);
            }

            sb.Clear();
            line = RemoveCommentIfHistorical(line);
            if (!string.IsNullOrEmpty(line) && line.StartsWith("msgstr"))
            {
                var msgstr = Unquote(line);
                sb.Append(msgstr);

                while ((line = fs.ReadLine()) != null && (msgstr = Unquote(line)) != null)
                {
                    line = RemoveCommentIfHistorical(line);
                    sb.Append(msgstr);
                }

                message.Message = Unescape(sb.ToString());
            }
            return message;
        }

        /// <summary>
        /// Helper for writing either a msgid or msgstr to the po file.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="hasReferences"></param>
        /// <param name="type">"msgid" or "msgstr"</param>
        /// <param name="value"></param>
        private static void WriteString(StreamWriter stream, bool hasReferences, string type, string value)
        {
            // Logic for outputting multi-line msgid.
            //
            // IN : a<LF>b
            // OUT: msgid ""
            //      "a\n"
            //      "b"
            //
            // IN : a<LF>b<LF>
            // OUT: msgid ""
            // OUT: "a\n"
            //      "b\n"
            //
            value = value ?? "";
            value = value.Replace("\r\n", "\n");
            StringBuilder sb = new StringBuilder(100);
            // If multi-line
            if (value.Contains('\n'))
            {
                // · msgid ""
                sb.AppendFormat("{0} \"\"\r\n", type);
                // · following lines
                sb.Append("\"");
                string s1 = value.Replace("\n", "\\n\"\r\n\"");
                sb.Append(s1);
                sb.Append("\"");
            }
            // If single-line
            else
            {
                sb.AppendFormat("{0} \"{1}\"", type, value);
            }
            // If noref...prefix each line with "#~ ".
            if (!hasReferences)
            {
                sb.Insert(0, "#~ ");
                sb.Replace("\r\n", "\r\n#~ ");
            }
            //
            string s = sb.ToString();
            stream.WriteLine(s);
        }

        #region quoting and escaping

        //this method removes anything before the first quote and also removes first and last quote
        private string Unquote(string lhs, string quotechar = "\"")
        {
            int begin = lhs.IndexOf(quotechar);
            if (begin == -1)
            {
                return null;
            }
            int end = lhs.LastIndexOf(quotechar);
            if (end <= begin)
            {
                return null;
            }
            return lhs.Substring(begin + 1, end - begin - 1);
        }

        private string escape(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }
            return s.Replace("\"", "\\\"");
        }

        /// <summary>
        /// Looks up in the subject string standard C escape sequences and converts them
        /// to their actual character counterparts.
        /// </summary>
        /// <seealso href="http://stackoverflow.com/questions/6629020/evaluate-escaped-string/8854626#8854626"/>
        private string Unescape(string s)
        {
            Regex regex_unescape = new Regex("\\\\[abfnrtv?\"'\\\\]|\\\\[0-3]?[0-7]{1,2}|\\\\u[0-9a-fA-F]{4}|.",
                RegexOptions.Singleline);

            StringBuilder sb = new StringBuilder();
            MatchCollection mc = regex_unescape.Matches(s, 0);

            foreach (Match m in mc)
            {
                if (m.Length == 1)
                {
                    sb.Append(m.Value);
                }
                else
                {
                    if (m.Value[1] >= '0' && m.Value[1] <= '7')
                    {
                        int i = 0;

                        for (int j = 1; j < m.Length; j++)
                        {
                            i *= 8;
                            i += m.Value[j] - '0';
                        }

                        sb.Append((char) i);
                    }
                    else if (m.Value[1] == 'u')
                    {
                        int i = 0;

                        for (int j = 2; j < m.Length; j++)
                        {
                            i *= 16;

                            if (m.Value[j] >= '0' && m.Value[j] <= '9')
                            {
                                i += m.Value[j] - '0';
                            }
                            else if (m.Value[j] >= 'A' && m.Value[j] <= 'F')
                            {
                                i += m.Value[j] - 'A' + 10;
                            }
                            else if (m.Value[j] >= 'a' && m.Value[j] <= 'a')
                            {
                                i += m.Value[j] - 'a' + 10;
                            }
                        }

                        sb.Append((char) i);
                    }
                    else
                    {
                        switch (m.Value[1])
                        {
                            case 'a':
                                sb.Append('\a');
                                break;
                            case 'b':
                                sb.Append('\b');
                                break;
                            case 'f':
                                sb.Append('\f');
                                break;
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            case 't':
                                sb.Append('\t');
                                break;
                            case 'v':
                                sb.Append('\v');
                                break;
                            default:
                                sb.Append(m.Value[1]);
                                break;
                        }
                    }
                }
            }

            return sb.ToString();
        }

        #endregion

        private static void CreateFileIfNotExists(string file)
        {
            if (!File.Exists(file))
            {
                var fileInfo = new FileInfo(file);
                var dirInfo = new DirectoryInfo(Path.GetDirectoryName(file));

                if (!dirInfo.Exists)
                {
                    dirInfo.Create();
                }

                fileInfo.Create().Close();
            }
        }

        private static void CreateBackupFile(string file)
        {
            var backupFile = $"{file}.backup";

            if (File.Exists(file)) //we backup one version. more advanced backup solutions could be added here.
            {
                if (File.Exists(backupFile))
                {
                    File.Delete(backupFile);
                }

                File.Move(file, backupFile);
                File.Delete(file);
            }
        }

        private bool FilesTheSame(string fileOne, string fileTwo, int skip = 4)
        {
            if (!File.Exists(fileOne) || !File.Exists(fileTwo)) return false;

            var newContent = File.ReadAllLines(fileOne).Skip(skip).ToList();
            var oldContent = File.ReadAllLines(fileTwo).Skip(skip).ToList();

            return oldContent.Count != 0 && newContent.Zip(oldContent, (n, o) => o != null && o.Equals(n)).All(b => b);
        }

        private static void OutputHeader(StreamWriter stream, string potDate = null)
        {
            stream.WriteLine("msgid \"\"");
            stream.WriteLine("msgstr \"\"");
            stream.WriteLine("\"Project-Id-Version: \\n\"");
            stream.WriteLine(string.IsNullOrWhiteSpace(potDate) ? $"\"POT-Creation-Date: {DateTime.Now:yyyy-MM-dd HH:mmzzz}\\n\"" : potDate);
            if (!string.IsNullOrWhiteSpace(potDate))
            {
                stream.WriteLine($"\"PO-Revision-Date: {DateTime.Now:yyyy-MM-dd HH:mmzzz}\\n\"");
            }
            stream.WriteLine("\"MIME-Version: 1.0\\n\"");
            stream.WriteLine("\"Content-Type: text/plain; charset=utf-8\\n\"");
            stream.WriteLine("\"Content-Transfer-Encoding: 8bit\\n\"");
            stream.WriteLine("\"X-Generator: i18n.POTGenerator\\n\"");
            stream.WriteLine();
        }

        #endregion
    }
}