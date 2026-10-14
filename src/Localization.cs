using System;
using System.Collections.Generic;
using System.IO;
using DV.Localization;
using DVLangHelper.Runtime;

namespace DVLSFG {
    internal static class L10n {
        static TranslationInjector source;
        static readonly Dictionary<string,string> english=new Dictionary<string,string>();
        internal static void Initialize(string directory) {
            if(source!=null) return;
            string file=Path.Combine(directory,"translations.csv");
            if(!File.Exists(file)) throw new FileNotFoundException("Translation CSV missing",file);
            source=new TranslationInjector("dv_scaling");
            source.AddTranslationsFromCsv(file);
            foreach(var term in source.Terms) {
                if(term.Languages.Length>0 && !string.IsNullOrEmpty(term.Languages[0])) english[term.Term]=term.Languages[0];
            }
            if(english.Count==0) throw new InvalidDataException("Translation CSV contains no English terms");
        }
        internal static string Get(string key) {
            // During early UMM startup the game's sources may not be injected yet.
            // Keep the fallback in the CSV too, rather than embedding UI text.
            string fallback;
            if(!TranslationInjector.InjectionStarted && english.TryGetValue(key,out fallback)) return fallback;
            string translated=LocalizationAPI.L(key);
            return !string.IsNullOrEmpty(translated) && translated!=key ? translated : english.TryGetValue(key,out fallback) ? fallback : key;
        }
    }
}
