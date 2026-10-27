// UI languages: Turkish, English, Spanish, German, French, Chinese (中文).
// Strings are fetched with Loc.T("key") and change with the selected language.
// If a translation is missing, English is shown, then Turkish.
using System;
using System.Collections.Generic;
using System.Globalization;

static class Loc
{
    public static readonly string[] Codes = { "tr", "en", "es", "de", "fr", "zh" };
    public static readonly string[] Names = { "Türkçe", "English", "Español", "Deutsch", "Français", "中文" };
    public static string Current = "tr";

    // Order: tr, en, es, de, fr, zh
    static readonly Dictionary<string, string[]> table = new Dictionary<string, string[]>
    {
        { "app_title", new[] { "Güç Modu", "Power Mode", "Modo de energía", "Energiemodus", "Mode d'énergie", "电源模式" } },
        { "app_subtitle", new[] { "Tek tıkla pil, maks tasarruf, denge veya performans", "One click for battery, max saver, balanced or performance", "Un clic: batería, máximo ahorro, equilibrado o rendimiento", "Ein Klick: Akku, max. Sparen, ausgewogen oder Leistung", "Un clic : batterie, économie max., équilibré ou performance", "一键切换电池、最大省电、均衡或性能" } },
        { "mode_maks", new[] { "Maksimum Tasarruf", "Maximum Saver", "Máximo ahorro", "Maximal sparen", "Économie maximale", "最大省电" } },
        { "mode_pil", new[] { "Pil Tasarrufu", "Battery Saver", "Ahorro de batería", "Akkusparmodus", "Économie de batterie", "电池省电" } },
        { "mode_normal", new[] { "Normal", "Normal", "Normal", "Normal", "Normal", "普通" } },
        { "mode_normal_long", new[] { "Normal (Dengeli)", "Normal (Balanced)", "Normal (Equilibrado)", "Normal (Ausgewogen)", "Normal (équilibré)", "普通（均衡）" } },
        { "mode_perf", new[] { "Performans", "Performance", "Rendimiento", "Leistung", "Performance", "性能" } },
        { "mode_other", new[] { "Diğer plan", "Other plan", "Otro plan", "Anderer Plan", "Autre plan", "其他方案" } },
        { "chip_maks", new[] { "Maks", "Max", "Máx", "Max", "Max", "最大" } },
        { "chip_pil", new[] { "Pil", "Battery", "Batería", "Akku", "Batt.", "电池" } },
        { "chip_denge", new[] { "Denge", "Balanced", "Equil.", "Ausgew.", "Équil.", "均衡" } },
        { "chip_perf", new[] { "Perf", "Perf", "Rend.", "Leist.", "Perf", "性能" } },
        { "chip_ayarlar", new[] { "Ayarlar", "Settings", "Ajustes", "Einstellungen", "Réglages", "设置" } },
        { "active_prefix", new[] { "Aktif: ", "Active: ", "Activo: ", "Aktiv: ", "Actif : ", "当前：" } },
        { "badge_active", new[] { "AKTİF", "ACTIVE", "ACTIVO", "AKTIV", "ACTIF", "当前" } },
        { "status_applying", new[] { "Güç modu uygulanıyor…", "Applying power mode…", "Aplicando modo de energía…", "Energiemodus wird angewendet…", "Application du mode d'énergie…", "正在应用电源模式…" } },
        { "error_prefix", new[] { "Hata: ", "Error: ", "Error: ", "Fehler: ", "Erreur : ", "错误：" } },
        { "balloon_hidden", new[] { "Tepsiye küçüldü. Açmak için simgeye çift tıklayın.", "Minimized to the tray. Double-click the icon to open.", "Minimizado a la bandeja. Haz doble clic en el icono para abrir.", "In den Infobereich minimiert. Doppelklick auf das Symbol zum Öffnen.", "Réduit dans la zone de notification. Double-cliquez sur l'icône pour l'ouvrir.", "已最小化到托盘。双击图标即可打开。" } },
        { "menu_open", new[] { "Aç", "Open", "Abrir", "Öffnen", "Ouvrir", "打开" } },
        { "menu_notch", new[] { "Çentik", "Notch", "Muesca", "Notch", "Encoche", "刘海" } },
        { "menu_exit", new[] { "Çıkış", "Exit", "Salir", "Beenden", "Quitter", "退出" } },
        { "already_running", new[] { "{0} zaten çalışıyor. Sistem tepsisindeki simgeye çift tıklayarak açabilirsiniz.", "{0} is already running. Double-click its tray icon to open it.", "{0} ya está en ejecución. Haz doble clic en su icono de la bandeja para abrirlo.", "{0} läuft bereits. Doppelklick auf das Infobereich-Symbol zum Öffnen.", "{0} est déjà lancé. Double-cliquez sur son icône dans la zone de notification.", "{0} 已在运行。双击系统托盘图标即可打开。" } },
        { "close", new[] { "Kapat", "Close", "Cerrar", "Schließen", "Fermer", "关闭" } },
        { "settings_title", new[] { "{0} Ayarları", "{0} Settings", "Ajustes de {0}", "{0} Einstellungen", "Réglages de {0}", "{0} 设置" } },
        { "lang_label", new[] { "Dil", "Language", "Idioma", "Sprache", "Langue", "语言" } },
        { "group_general", new[] { "Genel", "General", "General", "Allgemein", "Général", "常规" } },
        { "show_notch", new[] { "Çentiği göster", "Show notch", "Mostrar muesca", "Notch anzeigen", "Afficher l'encoche", "显示刘海" } },
        { "notch_center", new[] { "Çentiği ortala", "Center the notch", "Centrar la muesca", "Notch zentrieren", "Centrer l'encoche", "刘海居中" } },
        { "show_fps", new[] { "FPS göster (çentikte, yönetici modu gerekir)", "Show FPS (in the notch, needs admin mode)", "Mostrar FPS (en la muesca, requiere modo admin)", "FPS anzeigen (in der Notch, Admin-Modus nötig)", "Afficher les FPS (dans l'encoche, mode admin requis)", "显示 FPS（在刘海中，需要管理员模式）" } },
        { "pet_show", new[] { "Çentikte pet göster", "Show a pet in the notch", "Mostrar mascota en la muesca", "Haustier in der Notch anzeigen", "Afficher un animal dans l'encoche", "在刘海中显示宠物" } },
        { "show_gpu", new[] { "GPU sıcaklığını göster (çentikte, NVIDIA)", "Show GPU temperature (in the notch, NVIDIA)", "Mostrar temperatura de GPU (en la muesca, NVIDIA)", "GPU-Temperatur anzeigen (in der Notch, NVIDIA)", "Afficher la température GPU (dans l'encoche, NVIDIA)", "显示 GPU 温度（在刘海中，NVIDIA）" } },
        { "autostart", new[] { "Windows açılınca {0}'i başlat", "Start {0} with Windows", "Iniciar {0} con Windows", "{0} mit Windows starten", "Lancer {0} au démarrage de Windows", "Windows 启动时运行 {0}" } },
        { "group_power", new[] { "Pil ve güç", "Battery & power", "Batería y energía", "Akku & Energie", "Batterie et énergie", "电池与电源" } },
        { "row_bat_cpu", new[] { "Pil: işlemci üst sınırı (%)", "Battery: CPU limit (%)", "Batería: límite de CPU (%)", "Akku: CPU-Grenze (%)", "Batterie : limite CPU (%)", "电池：CPU 上限 (%)" } },
        { "row_bat_scr", new[] { "Pil: ekran parlaklığı (%)", "Battery: screen brightness (%)", "Batería: brillo de pantalla (%)", "Akku: Bildschirmhelligkeit (%)", "Batterie : luminosité de l'écran (%)", "电池：屏幕亮度 (%)" } },
        { "row_max_cpu", new[] { "Maks: işlemci üst sınırı (%)", "Max: CPU limit (%)", "Máx: límite de CPU (%)", "Max: CPU-Grenze (%)", "Max : limite CPU (%)", "最大省电：CPU 上限 (%)" } },
        { "row_max_scr", new[] { "Maks: ekran parlaklığı (%)", "Max: screen brightness (%)", "Máx: brillo de pantalla (%)", "Max: Bildschirmhelligkeit (%)", "Max : luminosité de l'écran (%)", "最大省电：屏幕亮度 (%)" } },
        { "max_refresh", new[] { "Maksimum modda ekranı 60 Hz yap", "Set the screen to 60 Hz in Maximum Saver", "Usar 60 Hz en Máximo ahorro", "Bildschirm im Max.-Sparmodus auf 60 Hz", "Écran à 60 Hz en économie maximale", "最大省电模式下屏幕设为 60 Hz" } },
        { "group_admin", new[] { "Yönetici modu", "Admin mode", "Modo admin", "Admin-Modus", "Mode admin", "管理员模式" } },
        { "admin_installed", new[] { "Yönetici: kurulu", "Admin: installed", "Admin: instalado", "Admin: installiert", "Admin : installé", "管理员：已安装" } },
        { "admin_needs", new[] { "Yönetici: kurulum gerekli", "Admin: setup required", "Admin: instalación necesaria", "Admin: Einrichtung nötig", "Admin : installation requise", "管理员：需要安装" } },
        { "admin_not", new[] { "Yönetici: kurulu değil", "Admin: not installed", "Admin: no instalado", "Admin: nicht installiert", "Admin : non installé", "管理员：未安装" } },
        { "admin_reinstall", new[] { "Yeniden kur", "Reinstall", "Reinstalar", "Neu installieren", "Réinstaller", "重新安装" } },
        { "admin_install", new[] { "Yönetici modunu kur", "Install admin mode", "Instalar modo admin", "Admin-Modus installieren", "Installer le mode admin", "安装管理员模式" } },
        { "admin_installing", new[] { "Kuruluyor…", "Installing…", "Instalando…", "Wird installiert…", "Installation…", "正在安装…" } },
        { "admin_fail", new[] { "Kurulum tamamlanamadı.", "Setup could not be completed.", "La instalación no se completó.", "Die Einrichtung wurde nicht abgeschlossen.", "L'installation n'a pas abouti.", "安装未完成。" } },
        { "admin_ok", new[] { "Yönetici modu kuruldu. {0}'i kapatıp yeniden açın; bundan sonra UAC sormadan yönetici olarak açılır.", "Admin mode installed. Close and reopen {0}; from now on it starts as admin without a UAC prompt.", "Modo admin instalado. Cierra y vuelve a abrir {0}; desde ahora se inicia como admin sin UAC.", "Admin-Modus installiert. {0} schließen und neu öffnen; ab jetzt startet es ohne UAC-Abfrage als Admin.", "Mode admin installé. Fermez puis rouvrez {0} ; il démarrera désormais en admin sans invite UAC.", "管理员模式已安装。请关闭并重新打开 {0}；之后将无需 UAC 提示以管理员身份启动。" } },
        { "admin_uac_denied", new[] { "Yönetici onayı verilmedi; görev kurulmadı.", "Admin approval was not given; task not created.", "No se concedió la aprobación de admin; tarea no creada.", "Admin-Freigabe nicht erteilt; Aufgabe nicht erstellt.", "Approbation admin refusée ; tâche non créée.", "未授予管理员权限；未创建任务。" } },
        { "admin_task_fail", new[] { "Yönetici görevi kurulamadı (kod {0}).", "Admin task could not be created (code {0}).", "No se pudo crear la tarea de admin (código {0}).", "Admin-Aufgabe konnte nicht erstellt werden (Code {0}).", "Tâche admin non créée (code {0}).", "无法创建管理员任务（代码 {0}）。" } },
        { "pil_applied", new[] { "Pil Tasarrufu aktif (pilde CPU %{0}, ekran %{1}, turbo kapalı).", "Battery Saver on (on battery: CPU {0}%, screen {1}%, turbo off).", "Ahorro de batería activo (con batería: CPU {0} %, pantalla {1} %, turbo desactivado).", "Akkusparmodus aktiv (im Akkubetrieb: CPU {0} %, Bildschirm {1} %, Turbo aus).", "Économie de batterie active (sur batterie : CPU {0} %, écran {1} %, turbo désactivé).", "电池省电已开启（电池供电时：CPU {0}%，屏幕 {1}%，已关闭涡轮）。" } },
        { "maks_applied", new[] { "Maksimum Tasarruf aktif (pilde CPU %{0}, ekran %{1}).", "Maximum Saver on (on battery: CPU {0}%, screen {1}%).", "Máximo ahorro activo (con batería: CPU {0} %, pantalla {1} %).", "Maximal sparen aktiv (im Akkubetrieb: CPU {0} %, Bildschirm {1} %).", "Économie maximale active (sur batterie : CPU {0} %, écran {1} %).", "最大省电已开启（电池供电时：CPU {0}%，屏幕 {1}%）。" } },
        { "perf_applied", new[] { "Performans modu aktif (işlemci tam hızda).", "Performance mode on (CPU at full speed).", "Modo rendimiento activo (CPU a máxima velocidad).", "Leistungsmodus aktiv (CPU mit voller Geschwindigkeit).", "Mode performance actif (CPU à pleine vitesse).", "性能模式已开启（CPU 全速）。" } },
        { "normal_applied", new[] { "Normal (Dengeli) plan aktif.", "Normal (Balanced) plan active.", "Plan Normal (Equilibrado) activo.", "Plan „Normal (Ausgewogen)“ aktiv.", "Plan Normal (équilibré) actif.", "普通（均衡）方案已启用。" } },
        { "unknown_mode", new[] { "Bilinmeyen mod: {0}", "Unknown mode: {0}", "Modo desconocido: {0}", "Unbekannter Modus: {0}", "Mode inconnu : {0}", "未知模式：{0}" } },
        { "copy_fail", new[] { "Güç planı kopyalanamadı.", "Could not copy the power plan.", "No se pudo copiar el plan de energía.", "Energieplan konnte nicht kopiert werden.", "Impossible de copier le plan d'énergie.", "无法复制电源方案。" } },
        { "powercfg_fail", new[] { "powercfg {0} başarısız oldu (kod {1})", "powercfg {0} failed (code {1})", "powercfg {0} falló (código {1})", "powercfg {0} fehlgeschlagen (Code {1})", "powercfg {0} a échoué (code {1})", "powercfg {0} 失败（代码 {1}）" } },
        { "display_fail", new[] { "ekran hızı değiştirilemedi: {0}", "could not change refresh rate: {0}", "no se pudo cambiar la frecuencia: {0}", "Bildwiederholrate konnte nicht geändert werden: {0}", "impossible de changer la fréquence : {0}", "无法更改刷新率：{0}" } },
        { "display_no60", new[] { "bu ekranda 60 Hz modu yok", "this screen has no 60 Hz mode", "esta pantalla no tiene modo de 60 Hz", "dieser Bildschirm hat keinen 60-Hz-Modus", "cet écran n'a pas de mode 60 Hz", "此屏幕没有 60 Hz 模式" } },
        { "display_change_fail", new[] { "ekran hızı değiştirilemedi", "could not change refresh rate", "no se pudo cambiar la frecuencia", "Bildwiederholrate konnte nicht geändert werden", "impossible de changer la fréquence", "无法更改刷新率" } },
        { "display_restore_fail", new[] { "ekran hızı geri alınamadı", "could not restore refresh rate", "no se pudo restaurar la frecuencia", "Bildwiederholrate konnte nicht zurückgesetzt werden", "impossible de rétablir la fréquence", "无法恢复刷新率" } },
        { "display_info_fail", new[] { "ekran bilgisi alınamadı", "could not read screen info", "no se pudo leer la info de pantalla", "Bildschirminformationen konnten nicht gelesen werden", "infos de l'écran illisibles", "无法读取屏幕信息" } },
        { "bat_none", new[] { "Pil bulunamadı (masaüstü)", "No battery found (desktop)", "No se encontró batería (escritorio)", "Kein Akku gefunden (Desktop)", "Aucune batterie (ordinateur fixe)", "未检测到电池（台式机）" } },
        { "bat_full", new[] { "Prizde, tam dolu", "Plugged in, fully charged", "Conectado, totalmente cargado", "Am Netz, voll geladen", "Branché, entièrement chargé", "已接通电源，已充满" } },
        { "bat_charging", new[] { "Prizde, şarj oluyor", "Plugged in, charging", "Conectado, cargando", "Am Netz, wird geladen", "Branché, en charge", "已接通电源，正在充电" } },
        { "bat_short_full", new[] { "dolu", "full", "lleno", "voll", "plein", "已满" } },
        { "bat_short_charging", new[] { "şarjda", "charging", "cargando", "lädt", "en charge", "充电中" } },
        { "bat_remaining", new[] { "Pilde · kalan {0} sa {1} dk", "On battery · {0} h {1} min left", "Con batería · quedan {0} h {1} min", "Akku · noch {0} Std. {1} Min.", "Sur batterie · reste {0} h {1} min", "电池供电 · 剩余 {0} 小时 {1} 分钟" } },
        { "bat_short_remaining", new[] { "{0}s {1}dk", "{0}h {1}m", "{0} h {1} min", "{0} Std. {1} Min.", "{0} h {1} min", "{0}时{1}分" } },
        { "bat_calc", new[] { "Pilde · süre hesaplanıyor", "On battery · estimating time", "Con batería · calculando tiempo", "Akku · Restzeit wird berechnet", "Sur batterie · autonomie en calcul", "电池供电 · 正在计算剩余时间" } },
        { "bat_calc_short", new[] { "hesaplanıyor", "estimating", "calculando", "wird berechnet", "calcul…", "计算中" } },
        { "sub_maks", new[] { "CPU %{0} • ekran %{1}{2}", "CPU {0}% • screen {1}%{2}", "CPU {0} % • pantalla {1} %{2}", "CPU {0} % • Bildschirm {1} %{2}", "CPU {0} % • écran {1} %{2}", "CPU {0}% • 屏幕 {1}%{2}" } },
        { "hz60", new[] { " • 60 Hz", " • 60 Hz", " • 60 Hz", " • 60 Hz", " • 60 Hz", " • 60 Hz" } },
        { "sub_pil", new[] { "CPU %{0} • turbo kapalı • ekran %{1}", "CPU {0}% • turbo off • screen {1}%", "CPU {0} % • turbo desact. • pantalla {1} %", "CPU {0} % • Turbo aus • Bildschirm {1} %", "CPU {0} % • turbo désactivé • écran {1} %", "CPU {0}% • 涡轮关闭 • 屏幕 {1}%" } },
        { "sub_normal", new[] { "Windows dengeli varsayılanı", "Windows balanced default", "Valor equilibrado de Windows", "Windows-Standard (ausgewogen)", "Valeur équilibrée de Windows", "Windows 默认均衡" } },
        { "sub_perf", new[] { "İşlemci her zaman tam hızda", "CPU always at full speed", "CPU siempre a máxima velocidad", "CPU immer mit voller Geschwindigkeit", "CPU toujours à pleine vitesse", "CPU 始终全速" } },
        { "group_fan", new[] { "Oyun modu", "Game mode", "Modo juego", "Spielmodus", "Mode jeu", "游戏模式" } },
        { "fan_show", new[] { "Oyun modunu göster (sekme ve çentik)", "Show game mode (tab and notch)", "Mostrar modo juego (pestaña y muesca)", "Spielmodus anzeigen (Reiter und Notch)", "Afficher le mode jeu (onglet et encoche)", "显示游戏模式（标签页和刘海）" } },
        { "fan_presets_group", new[] { "Fan hızları (oyun modu açıkken)", "Fan speeds (when game mode is on)", "Velocidades del ventilador (con modo juego)", "Lüftergeschwindigkeiten (bei Spielmodus)", "Vitesses du ventilateur (mode jeu activé)", "风扇转速（游戏模式开启时）" } },
        { "manual_rpm", new[] { "Manuel hız (rpm)", "Manual speed (rpm)", "Velocidad manual (rpm)", "Manuelle Drehzahl (rpm)", "Vitesse manuelle (tr/min)", "手动转速 (rpm)" } },
        { "safety_temp", new[] { "Güvenlik sıcaklığı (°C)", "Safety temperature (°C)", "Temperatura de seguridad (°C)", "Sicherheitstemperatur (°C)", "Température de sécurité (°C)", "安全温度 (°C)" } },
        { "manual_apply", new[] { "Manuel hızı uygula", "Apply manual speed", "Aplicar velocidad manual", "Manuelle Drehzahl anwenden", "Appliquer la vitesse manuelle", "应用手动转速" } },
        { "tab_power", new[] { "Güç", "Power", "Energía", "Energie", "Énergie", "电源" } },
        { "tab_fan", new[] { "Fan", "Fan", "Ventilador", "Lüfter", "Ventilateur", "风扇" } },
        { "fan_auto_menu", new[] { "Fan otomatik (BIOS)", "Fan automatic (BIOS)", "Ventilador automático (BIOS)", "Lüfter automatisch (BIOS)", "Ventilateur automatique (BIOS)", "风扇自动 (BIOS)" } },
        { "auto_bios_btn", new[] { "Otomatik (BIOS'a geri ver)", "Automatic (return to BIOS)", "Automático (devolver a BIOS)", "Automatik (an BIOS zurückgeben)", "Automatique (rendre au BIOS)", "自动（交还 BIOS 控制）" } },
        { "preset_quiet", new[] { "Sessiz", "Quiet", "Silencioso", "Leise", "Silencieux", "静音" } },
        { "preset_mid", new[] { "Orta", "Medium", "Medio", "Mittel", "Moyen", "中速" } },
        { "preset_game", new[] { "Oyun", "Game", "Juego", "Spiel", "Jeu", "游戏" } },
        { "preset_max", new[] { "Max", "Max", "Máx", "Max", "Max", "最大" } },
        { "mode_auto", new[] { "Oto", "Auto", "Auto", "Auto", "Auto", "自动" } },
        { "mode_manual", new[] { "Manuel", "Manual", "Manual", "Manuell", "Manuel", "手动" } },
        { "mode_auto_long", new[] { "Otomatik", "Automatic", "Automático", "Automatisch", "Automatique", "自动" } },
        { "bios_no_reply", new[] { "HP BIOS yanıt vermedi; yönetici olarak çalıştırdığınızdan emin olun", "HP BIOS did not respond; make sure you run it as admin", "La BIOS de HP no respondió; ejecútalo como admin", "HP-BIOS antwortet nicht; als Admin starten", "Le BIOS HP n'a pas répondu ; lancez-le en admin", "HP BIOS 无响应；请确保以管理员身份运行" } },
        { "bios_no_reply_short", new[] { "HP BIOS yanıt vermedi", "HP BIOS did not respond", "La BIOS de HP no respondió", "HP-BIOS antwortet nicht", "Le BIOS HP n'a pas répondu", "HP BIOS 无响应" } },
        { "quiet_note", new[] { "Sessiz: fanlar durur, yük altında ısınabilir", "Quiet: fans stop, may heat up under load", "Silencioso: los ventiladores se detienen, puede calentarse con carga", "Leise: Lüfter stehen still, können unter Last warm werden", "Silencieux : ventilateurs arrêtés, peut chauffer sous charge", "静音：风扇停转，高负载下可能发热" } },
        { "fan_applying", new[] { "{0} uygulanıyor…", "Applying {0}…", "Aplicando {0}…", "{0} wird angewendet…", "Application de {0}…", "正在应用 {0}…" } },
        { "fan_applied", new[] { "{0} ({1} rpm) uygulandı", "{0} ({1} rpm) applied", "{0} ({1} rpm) aplicado", "{0} ({1} U/min) angewendet", "{0} ({1} tr/min) appliqué", "已应用 {0}（{1} rpm）" } },
        { "manual_applying", new[] { "Manuel hız uygulanıyor…", "Applying manual speed…", "Aplicando velocidad manual…", "Manuelle Drehzahl wird angewendet…", "Application de la vitesse manuelle…", "正在应用手动转速…" } },
        { "manual_applied", new[] { "Manuel hız ({0} rpm) uygulandı", "Manual speed ({0} rpm) applied", "Velocidad manual ({0} rpm) aplicada", "Manuelle Drehzahl ({0} U/min) angewendet", "Vitesse manuelle ({0} tr/min) appliquée", "已应用手动转速（{0} rpm）" } },
        { "auto_applying", new[] { "Otomatik moda dönülüyor…", "Switching back to automatic…", "Volviendo al modo automático…", "Zurück zu automatisch…", "Retour en mode automatique…", "正在切换回自动…" } },
        { "auto_done", new[] { "Otomatik (BIOS) moda dönüldü", "Back to automatic (BIOS)", "Vuelto al modo automático (BIOS)", "Zurück zu automatisch (BIOS)", "Retour en mode automatique (BIOS)", "已切换回自动（BIOS）" } },
        { "safety_trip", new[] { "Güvenlik: sıcaklık {0}°C'yi geçti, fanlar otomatik moda döndü", "Safety: temperature passed {0}°C, fans returned to automatic", "Seguridad: la temperatura superó {0} °C, los ventiladores volvieron al automático", "Sicherheit: Temperatur über {0} °C, Lüfter zurück auf Automatik", "Sécurité : température au-delà de {0} °C, ventilateurs en automatique", "安全：温度超过 {0}°C，风扇已切换回自动" } },
        { "safety_active", new[] { "Güvenlik devrede: otomatik moda döndü", "Safety active: returned to automatic", "Seguridad activa: vuelto al automático", "Sicherheit aktiv: zurück auf Automatik", "Sécurité active : retour en automatique", "安全保护已启动：已切换回自动" } },
        { "bios_ok", new[] { "HP BIOS bağlı", "HP BIOS connected", "BIOS de HP conectada", "HP-BIOS verbunden", "BIOS HP connecté", "HP BIOS 已连接" } },
        { "bios_fail", new[] { "HP BIOS okunamadı (yönetici olarak çalıştırın)", "HP BIOS unreadable (run as admin)", "BIOS de HP ilegible (ejecutar como admin)", "HP-BIOS nicht lesbar (als Admin starten)", "BIOS HP illisible (lancer en admin)", "无法读取 HP BIOS（请以管理员身份运行）" } },
        { "fan_label", new[] { "Fan", "Fan", "Ventilador", "Lüfter", "Ventilateur", "风扇" } },
        { "fan_rpm_none", new[] { "-- rpm", "-- rpm", "-- rpm", "-- U/min", "-- tr/min", "-- rpm" } },
        { "setup_title", new[] { "{0} Kurulum", "{0} Setup", "Instalación de {0}", "{0} Setup", "Installation de {0}", "{0} 安装程序" } },
        { "setup_tag_np", new[] { "Güç modu, çentik ve oyun modu tek uygulamada.", "Power modes, notch and game mode in one app.", "Modos de energía, muesca y modo juego en una sola app.", "Energiemodi, Notch und Spielmodus in einer App.", "Modes d'énergie, encoche et mode jeu dans une seule app.", "电源模式、刘海和游戏模式，尽在一个应用。" } },
        { "setup_tag_pn", new[] { "Güç modu, çentik ve FPS göstergesi tek uygulamada.", "Power modes, notch and FPS counter in one app.", "Modos de energía, muesca y contador de FPS en una sola app.", "Energiemodi, Notch und FPS-Anzeige in einer App.", "Modes d'énergie, encoche et compteur FPS dans une seule app.", "电源模式、刘海和 FPS 显示，尽在一个应用。" } },
        { "setup_folder", new[] { "Kurulacak klasör", "Install folder", "Carpeta de instalación", "Installationsordner", "Dossier d'installation", "安装文件夹" } },
        { "setup_browse", new[] { "Gözat", "Browse", "Examinar", "Ändern", "Parcourir", "浏览" } },
        { "setup_desktop", new[] { "Masaüstü kısayolu oluştur", "Create a desktop shortcut", "Crear acceso directo en el escritorio", "Desktop-Verknüpfung erstellen", "Créer un raccourci sur le bureau", "创建桌面快捷方式" } },
        { "setup_admin", new[] { "Yönetici modunu kur (bir kez Windows onayı ister)", "Install admin mode (Windows asks for approval once)", "Instalar modo admin (Windows pide permiso una vez)", "Admin-Modus einrichten (Windows fragt einmal nach)", "Installer le mode admin (Windows demande une fois)", "安装管理员模式（Windows 只询问一次）" } },
        { "setup_run", new[] { "Kurulumdan sonra {0}'i aç", "Open {0} after setup", "Abrir {0} al terminar", "{0} nach der Installation starten", "Ouvrir {0} après l'installation", "安装完成后打开 {0}" } },
        { "setup_info", new[] { "Windows açılışında başlatma varsayılan olarak açık kurulur; Ayarlar'dan kapatılabilir.", "Start with Windows is turned on by default; you can turn it off in Settings.", "El inicio con Windows se activa por defecto; puedes desactivarlo en Ajustes.", "Start mit Windows ist standardmäßig aktiv; in den Einstellungen abschaltbar.", "Le démarrage avec Windows est activé par défaut ; désactivable dans les Réglages.", "默认随 Windows 启动；可在设置中关闭。" } },
        { "setup_install", new[] { "Kur", "Install", "Instalar", "Installieren", "Installer", "安装" } },
        { "setup_pick_folder", new[] { "{0}'in kurulacağı klasörü seç (içine {0} klasörü açılır)", "Choose where to install {0} (a {0} folder is created inside)", "Elige dónde instalar {0} (se crea una carpeta {0} dentro)", "Ordner für {0} wählen (darin wird ein Ordner {0} angelegt)", "Choisissez où installer {0} (un dossier {0} y sera créé)", "选择 {0} 的安装位置（将在其中创建 {0} 文件夹）" } },
        { "setup_need_folder", new[] { "Bir klasör seç.", "Choose a folder.", "Elige una carpeta.", "Wählen Sie einen Ordner.", "Choisissez un dossier.", "请选择一个文件夹。" } },
        { "setup_running", new[] { "{0} açık. Tepsiden kapatıp tekrar kur.", "{0} is running. Close it from the tray and try again.", "{0} está en ejecución. Ciérralo desde la bandeja e inténtalo de nuevo.", "{0} läuft. Über den Infobereich beenden und erneut versuchen.", "{0} est lancé. Fermez-le depuis la zone de notification et réessayez.", "{0} 正在运行。请从托盘退出后重试。" } },
        { "setup_copying", new[] { "Dosyalar kopyalanıyor…", "Copying files…", "Copiando archivos…", "Dateien werden kopiert…", "Copie des fichiers…", "正在复制文件…" } },
        { "setup_locked", new[] { "Klasör korumalı; Windows onayı bekleniyor…", "The folder is protected; waiting for Windows approval…", "La carpeta está protegida; esperando permiso de Windows…", "Ordner ist geschützt; warte auf Windows-Bestätigung…", "Dossier protégé ; en attente de l'autorisation de Windows…", "文件夹受保护；正在等待 Windows 确认…" } },
        { "setup_admin_busy", new[] { "Yönetici modu kuruluyor; Windows onayı bekleniyor…", "Installing admin mode; waiting for Windows approval…", "Instalando modo admin; esperando permiso de Windows…", "Admin-Modus wird eingerichtet; warte auf Windows-Bestätigung…", "Installation du mode admin ; en attente de l'autorisation de Windows…", "正在安装管理员模式；正在等待 Windows 确认…" } },
        { "setup_driver_busy", new[] { "İşlemci sıcaklığı sürücüsü kuruluyor…", "Installing the CPU temperature driver…", "Instalando el controlador de temperatura de la CPU…", "CPU-Temperaturtreiber wird installiert…", "Installation du pilote de température du processeur…", "正在安装 CPU 温度驱动…" } },
        { "setup_done", new[] { "Kurulum tamamlandı:\n{0}", "Setup complete:\n{0}", "Instalación completada:\n{0}", "Installation abgeschlossen:\n{0}", "Installation terminée :\n{0}", "安装完成：\n{0}" } },
        { "setup_failed", new[] { "Kurulum başarısız: {0}", "Setup failed: {0}", "La instalación falló: {0}", "Installation fehlgeschlagen: {0}", "Échec de l'installation : {0}", "安装失败：{0}" } },
        { "setup_no_payload", new[] { "Kurulum dosyasının içinde {0} bulunamadı.", "{0} was not found inside the setup file.", "No se encontró {0} dentro del instalador.", "{0} wurde in der Setup-Datei nicht gefunden.", "{0} est introuvable dans le fichier d'installation.", "安装文件中未找到 {0}。" } },
        { "setup_copy_fail", new[] { "Dosyalar yazılamadı (kod {0}).", "Could not write the files (code {0}).", "No se pudieron escribir los archivos (código {0}).", "Dateien konnten nicht geschrieben werden (Code {0}).", "Impossible d'écrire les fichiers (code {0}).", "无法写入文件（代码 {0}）。" } },
        { "setup_copy_denied", new[] { "Yönetici onayı verilmedi; dosyalar güncellenmedi.", "Admin approval was not given; the files were not updated.", "No se concedió permiso de administrador; los archivos no se actualizaron.", "Keine Admin-Bestätigung; Dateien wurden nicht aktualisiert.", "Autorisation admin refusée ; les fichiers n'ont pas été mis à jour.", "未获得管理员批准；文件未更新。" } },
        { "setup_pawn_missing", new[] { "İşlemci sıcaklığı sürücüsünün kurulum paketi bulunamadı.", "The CPU temperature driver package was not found.", "No se encontró el paquete del controlador de temperatura de la CPU.", "Paket des CPU-Temperaturtreibers nicht gefunden.", "Paquet du pilote de température du processeur introuvable.", "未找到 CPU 温度驱动安装包。" } },
        { "setup_pawn_fail", new[] { "İşlemci sıcaklığı sürücüsü kurulamadı (kod {0}).", "The CPU temperature driver could not be installed (code {0}).", "No se pudo instalar el controlador de temperatura de la CPU (código {0}).", "CPU-Temperaturtreiber konnte nicht installiert werden (Code {0}).", "Le pilote de température du processeur n'a pas pu être installé (code {0}).", "无法安装 CPU 温度驱动（代码 {0}）。" } },
        { "setup_pawn_denied", new[] { "Yönetici onayı verilmedi; işlemci sıcaklığı sürücüsü kurulmadı.", "Admin approval was not given; the CPU temperature driver was not installed.", "No se concedió permiso de administrador; el controlador de temperatura no se instaló.", "Keine Admin-Bestätigung; CPU-Temperaturtreiber wurde nicht installiert.", "Autorisation admin refusée ; le pilote de température n'a pas été installé.", "未获得管理员批准；CPU 温度驱动未安装。" } },
        { "setup_task_start_fail", new[] { "Yönetici görevi başlatılamadı. {0} normal modda çalışır.", "The admin task could not be started. {0} will run in normal mode.", "No se pudo iniciar la tarea de administrador. {0} se ejecutará en modo normal.", "Admin-Aufgabe konnte nicht gestartet werden. {0} läuft im normalen Modus.", "Impossible de lancer la tâche admin. {0} fonctionnera en mode normal.", "无法启动管理员任务。{0} 将以普通模式运行。" } },
        { "setup_task_fail", new[] { "Yönetici görevi kurulamadı (kod {1}). {0} normal modda çalışır.", "The admin task could not be installed (code {1}). {0} will run in normal mode.", "No se pudo instalar la tarea de administrador (código {1}). {0} se ejecutará en modo normal.", "Admin-Aufgabe konnte nicht eingerichtet werden (Code {1}). {0} läuft im normalen Modus.", "Impossible d'installer la tâche admin (code {1}). {0} fonctionnera en mode normal.", "无法安装管理员任务（代码 {1}）。{0} 将以普通模式运行。" } },
        { "setup_task_denied", new[] { "Yönetici onayı verilmedi. {0} normal modda çalışır; Ayarlar'dan sonra tekrar kurabilirsin.", "Admin approval was not given. {0} will run in normal mode; you can install it later in Settings.", "No se concedió permiso de administrador. {0} se ejecutará en modo normal; puedes instalarlo luego en Ajustes.", "Keine Admin-Bestätigung. {0} läuft im normalen Modus; später in den Einstellungen einrichtbar.", "Autorisation admin refusée. {0} fonctionnera en mode normal ; vous pourrez l'installer plus tard dans les Réglages.", "未获得管理员批准。{0} 将以普通模式运行；之后可在设置中安装。" } },
        { "uninst_title", new[] { "{0} Kaldır", "Uninstall {0}", "Desinstalar {0}", "{0} deinstallieren", "Désinstaller {0}", "卸载 {0}" } },
        { "uninst_ask", new[] { "{0} kaldırılsın mı?\n\nProgram klasörü, kısayollar, yönetici görevi ve ayarlar silinecek.", "Uninstall {0}?\n\nThe program folder, shortcuts, admin task and settings will be deleted.", "¿Desinstalar {0}?\n\nSe eliminarán la carpeta del programa, los accesos directos, la tarea de administrador y los ajustes.", "{0} deinstallieren?\n\nProgrammordner, Verknüpfungen, Admin-Aufgabe und Einstellungen werden gelöscht.", "Désinstaller {0} ?\n\nLe dossier du programme, les raccourcis, la tâche admin et les réglages seront supprimés.", "要卸载 {0} 吗？\n\n程序文件夹、快捷方式、管理员任务和设置都将被删除。" } },
        { "uninst_running", new[] { "{0} açık. Tepsiden kapatıp kaldırmayı tekrar deneyin.", "{0} is running. Close it from the tray and try uninstalling again.", "{0} está en ejecución. Ciérralo desde la bandeja y vuelve a intentarlo.", "{0} läuft. Über den Infobereich beenden und erneut deinstallieren.", "{0} est lancé. Fermez-le depuis la zone de notification et réessayez.", "{0} 正在运行。请从托盘退出后再次尝试卸载。" } },
        { "uninst_done", new[] { "{0} kaldırıldı.", "{0} has been uninstalled.", "{0} se ha desinstalado.", "{0} wurde deinstalliert.", "{0} a été désinstallé.", "{0} 已卸载。" } },
        { "dlg_yes", new[] { "Evet", "Yes", "Sí", "Ja", "Oui", "是" } },
        { "dlg_no", new[] { "Hayır", "No", "No", "Nein", "Non", "否" } },
        { "dlg_ok", new[] { "Tamam", "OK", "Aceptar", "OK", "OK", "确定" } }
    };

    // Text: selected language -> English -> Turkish
    public static string T(string key)
    {
        string[] row;
        if (!table.TryGetValue(key, out row)) return key;
        int i = Array.IndexOf(Codes, Current);
        if (i >= 0 && !string.IsNullOrEmpty(row[i])) return row[i];
        return string.IsNullOrEmpty(row[1]) ? row[0] : row[1];
    }

    // Formatted text: Loc.F("key", arg0, arg1, ...)
    public static string F(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    // Chinese text does not render in Segoe UI; Microsoft YaHei UI is used for that language
    public static string Face(string face)
    {
        return Current == "zh" && face.StartsWith("Segoe") ? "Microsoft YaHei UI" : face;
    }

    // English if the Windows language is not supported
    public static string Detect()
    {
        string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Array.IndexOf(Codes, two) >= 0 ? two : "en";
    }

    public static void Set(string code)
    {
        Current = Array.IndexOf(Codes, code) >= 0 ? code : "en";
    }
}
