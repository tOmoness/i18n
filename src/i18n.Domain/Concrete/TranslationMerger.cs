using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using i18n.Domain.Entities;
using i18n.Domain.Abstract;


namespace i18n.Domain.Concrete
{
    public class TranslationMerger
    {
        private readonly ITranslationRepository _repository;
        private readonly i18nSettings _settings;

        public TranslationMerger(ITranslationRepository repository, i18nSettings settings)
        {
            _repository = repository;
            _settings = settings;
        }

        public void MergeTranslation(IDictionary<string, TemplateItem> src, Translation dst)
        {
            // Our purpose here is to merge newly parsed message items (src) with those already stored in a translation repo (dst).
            // 1. Where an orphan msgid is found (present in the dst but not the src) we update it in the dst to remove all references.
            // 2. Where a src msgid is missing from dst, we simply ADD it to dst.
            // 3. Where a src msgid is present in dst, we update the item in the dst to match the src (references, comments, etc.).
            //
            // 1.
            // Simply remove all references from dst items, for now.
            foreach (TranslationItem dstItem in dst.Items.Values)
            {
                dstItem.References = null;
            }

            var fileNameList = new List<string>();

            // 2. and 3.
            foreach (TemplateItem srcItem in src.Values)
            {
                TranslationItem dstItem = dst.Items.GetOrAdd(srcItem.MsgKey, k => new TranslationItem { MsgKey = srcItem.MsgKey });
                dstItem.MsgId = srcItem.MsgId;
                dstItem.References = srcItem.References;
                dstItem.ExtractedComments = srcItem.Comments;

                if (_settings.GenerateTemplatePerFile)
                {
                    if (!fileNameList.Contains(srcItem.FileName))
                        fileNameList.Add(srcItem.FileName);
                    dstItem.FileName = srcItem.FileName;
                }
            }

            // Persist changes.
            _repository.SaveTranslation(dst, fileNameList);
        }

        public void MergeAllTranslation(IDictionary<string, TemplateItem> items)
        {
            foreach (var language in _repository.GetAvailableLanguages())
            {
                var filesNames = items.GroupBy(x => x.Value.FileName).Select(x => x.Key).ToList();
                MergeTranslation(items, _repository.GetTranslation(language.LanguageShortTag, filesNames, false));
            }
        }

    }
}
