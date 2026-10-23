using PreySense.Frontend.Services;

namespace PreySense.Tests;

[TestClass]
public class LocalizationTests
{
    [TestMethod]
    public void Test_Five_Languages_Translate_Labels_And_Dialogs()
    {
        string previous = LocalizationService.CurrentLanguage;
        try
        {
            LocalizationService.SetLanguage("en-US", persist: false);
            Assert.AreEqual("Quit", LocalizationService.Translate("Quit"));
            Assert.AreEqual("PreySense • Lighting", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(0, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("zh-CN", persist: false);
            Assert.AreEqual("退出", LocalizationService.Translate("Quit"));
            Assert.AreEqual("电池充电限制", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("混合", LocalizationService.Translate("Hybrid"));
            Assert.AreEqual("关闭", LocalizationService.Translate("Off"));
            Assert.AreEqual("无", LocalizationService.Translate("None"));
            Assert.AreEqual("PreySense • 灯光", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(" • 硬件显示切换", LocalizationService.Translate(" • Hardware Display MUX"));
            Assert.AreEqual("正在读取节能…", LocalizationService.Format("Reading {0}...", LocalizationService.Translate("Eco")));
            Assert.AreEqual(1, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("hi-IN", persist: false);
            Assert.AreEqual("बाहर निकलें", LocalizationService.Translate("Quit"));
            Assert.AreNotEqual("Quit", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual(2, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("es-ES", persist: false);
            Assert.AreEqual("Salir", LocalizationService.Translate("Quit"));
            Assert.AreEqual("Límite de batería", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("¿Restablecer Equilibrado a los ajustes de fábrica y aplicarlos ahora?",
                LocalizationService.Format("Reset {0} to factory power settings and apply them now?", LocalizationService.Translate("Balanced")));
            Assert.AreEqual(3, LocalizationService.LanguageIndex);

            LocalizationService.SetLanguage("fr-FR", persist: false);
            Assert.AreEqual("Quitter", LocalizationService.Translate("Quit"));
            Assert.AreEqual("Limite de batterie", LocalizationService.Translate("Battery Limit"));
            Assert.AreEqual("PreySense • Éclairage", LocalizationService.Translate("PreySense • Lighting"));
            Assert.AreEqual(4, LocalizationService.LanguageIndex);

            Assert.AreEqual("en-US", LocalizationService.Normalize("en-GB"));
            Assert.AreEqual("zh-CN", LocalizationService.Normalize("zh-CN"));
            Assert.AreEqual("hi-IN", LocalizationService.Normalize("hi"));
            Assert.AreEqual("es-ES", LocalizationService.Normalize("es-MX"));
            Assert.AreEqual("fr-FR", LocalizationService.Normalize("fr-CA"));
        }
        finally
        {
            LocalizationService.SetLanguage(previous, persist: false);
        }
    }
}
