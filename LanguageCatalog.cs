internal static class LanguageCatalog
{
    internal static readonly Dictionary<string, string[]> UiWords = new()
    {
        ["zh-CN"] = new[] { "Translate", "请输入要翻译的内容...", "译文", "翻译 · F5", "隐藏窗口 · F4", "置顶", "字符数", "选择窗口背景" },
        ["en"] = new[] { "Translate", "Enter text to translate...", "Translation", "Translate · F5", "Hide · F4", "Always on top", "Character count", "Choose background" },
        ["ja"] = new[] { "Translate", "翻訳するテキストを入力...", "翻訳結果", "翻訳 · F5", "非表示 · F4", "最前面に表示", "文字数", "背景を選択" },
        ["ko"] = new[] { "Translate", "번역할 내용을 입력하세요...", "번역 결과", "번역 · F5", "숨기기 · F4", "항상 위에", "글자 수", "배경 선택" },
        ["fr"] = new[] { "Translate", "Saisissez le texte à traduire...", "Traduction", "Traduire · F5", "Masquer · F4", "Toujours au premier plan", "Nombre de caractères", "Choisir l’arrière-plan" },
        ["de"] = new[] { "Translate", "Text zum Übersetzen eingeben...", "Übersetzung", "Übersetzen · F5", "Ausblenden · F4", "Immer im Vordergrund", "Zeichenanzahl", "Hintergrund wählen" },
        ["es"] = new[] { "Translate", "Escribe el texto que quieres traducir...", "Traducción", "Traducir · F5", "Ocultar · F4", "Siempre visible", "Recuento de caracteres", "Elegir fondo" },
        ["it"] = new[] { "Translate", "Inserisci il testo da tradurre...", "Traduzione", "Traduci · F5", "Nascondi · F4", "Sempre in primo piano", "Conteggio caratteri", "Scegli sfondo" },
        ["pt"] = new[] { "Translate", "Digite o texto para traduzir...", "Tradução", "Traduzir · F5", "Ocultar · F4", "Sempre no topo", "Contagem de caracteres", "Escolher fundo" },
        ["ru"] = new[] { "Translate", "Введите текст для перевода...", "Перевод", "Перевести · F5", "Скрыть · F4", "Поверх всех окон", "Количество символов", "Выбрать фон" },
        ["ar"] = new[] { "Translate", "أدخل النص للترجمة...", "الترجمة", "ترجم · F5", "إخفاء · F4", "دائماً في المقدمة", "عدد الأحرف", "اختر الخلفية" },
        ["hi"] = new[] { "Translate", "अनुवाद के लिए टेक्स्ट दर्ज करें...", "अनुवाद", "अनुवाद करें · F5", "छिपाएँ · F4", "हमेशा ऊपर", "अक्षर संख्या", "पृष्ठभूमि चुनें" },
        ["zh-TW"] = new[] { "Translate", "請輸入要翻譯的內容...", "譯文", "翻譯 · F5", "隱藏視窗 · F4", "置頂", "字元數", "選擇背景" }
    };

    internal static readonly (string Name, string Code)[] Languages =
    {
        ("自动检测", "autodetect"), ("英语 · English", "en"), ("简体中文", "zh-CN"), ("繁體中文", "zh-TW"),
        ("日语 · 日本語", "ja"), ("韩语 · 한국어", "ko"), ("法语 · Français", "fr"), ("德语 · Deutsch", "de"),
        ("西班牙语 · Español", "es"), ("意大利语 · Italiano", "it"), ("葡萄牙语 · Português", "pt"), ("俄语 · Русский", "ru"),
        ("阿拉伯语 · العربية", "ar"), ("泰语 · ไทย", "th"), ("越南语 · Tiếng Việt", "vi"), ("荷兰语 · Nederlands", "nl"),
        ("波兰语 · Polski", "pl"), ("土耳其语 · Türkçe", "tr"), ("印尼语 · Bahasa Indonesia", "id"), ("印地语 · हिन्दी", "hi"),
        ("孟加拉语 · বাংলা", "bn"), ("乌尔都语 · اردو", "ur"), ("波斯语 · فارسی", "fa"), ("希伯来语 · עברית", "he"),
        ("希腊语 · Ελληνικά", "el"), ("瑞典语 · Svenska", "sv"), ("挪威语 · Norsk", "no"), ("丹麦语 · Dansk", "da"),
        ("芬兰语 · Suomi", "fi"), ("捷克语 · Čeština", "cs"), ("斯洛伐克语 · Slovenčina", "sk"), ("匈牙利语 · Magyar", "hu"),
        ("罗马尼亚语 · Română", "ro"), ("保加利亚语 · Български", "bg"), ("乌克兰语 · Українська", "uk"), ("塞尔维亚语 · Српски", "sr"),
        ("克罗地亚语 · Hrvatski", "hr"), ("斯洛文尼亚语 · Slovenščina", "sl"), ("立陶宛语 · Lietuvių", "lt"), ("拉脱维亚语 · Latviešu", "lv"),
        ("爱沙尼亚语 · Eesti", "et"), ("马来语 · Bahasa Melayu", "ms"), ("菲律宾语 · Filipino", "tl"), ("斯瓦希里语 · Kiswahili", "sw"),
        ("南非荷兰语 · Afrikaans", "af"), ("冰岛语 · Íslenska", "is"), ("爱尔兰语 · Gaeilge", "ga"), ("加泰罗尼亚语 · Català", "ca"),
        ("巴斯克语 · Euskara", "eu"), ("加利西亚语 · Galego", "gl"), ("亚美尼亚语 · Հայերեն", "hy"), ("格鲁吉亚语 · ქართული", "ka"),
        ("阿塞拜疆语 · Azərbaycan", "az"), ("哈萨克语 · Қазақша", "kk"), ("乌兹别克语 · Oʻzbek", "uz"), ("蒙古语 · Монгол", "mn"),
        ("尼泊尔语 · नेपाली", "ne"), ("僧伽罗语 · සිංහල", "si"), ("高棉语 · ខ្មែរ", "km"), ("缅甸语 · မြန်မာ", "my"),
        ("老挝语 · ລາວ", "lo"), ("马拉地语 · मराठी", "mr"), ("泰米尔语 · தமிழ்", "ta"), ("泰卢固语 · తెలుగు", "te"),
        ("古吉拉特语 · ગુજરાતી", "gu"), ("旁遮普语 · ਪੰਜਾਬੀ", "pa"), ("约鲁巴语 · Yorùbá", "yo"), ("豪萨语 · Hausa", "ha"),
        ("阿姆哈拉语 · አማርኛ", "am")
    };
}
