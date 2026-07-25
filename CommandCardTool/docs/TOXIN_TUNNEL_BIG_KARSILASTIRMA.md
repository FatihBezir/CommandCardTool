# Toxin Tünel / Nuke kısayolları — kök neden ve çözüm

**Güncelleme:** 2026-07-25 — bu doküman 2026-06-22 sürümünün yerini alır.
Eski sürümdeki iki ana tespit **yanlıştı** (bkz. §6); ikisi de aşağıdaki `WRTS` hatasının yan etkisiydi.

**Karşılaştırılan dosyalar**

| Kaynak | Yol |
|--------|-----|
| Vanilla | `…\Command & Conquer Generals - Zero Hour\EnglishZH.big` |
| Çalışan referans (başka biri) | `…\!HotkeysLeikezeZH.big` (CSF) + `!HotkeysLeikezeIndicatorsZH.big` (atlas + INI) |
| Bozuk çıktı | `C:\Users\MSI\Downloads\!EnglishZH.big` |

---

## 1. Kök neden — `WRTS` etiketi tanınmıyordu

CSF'te metin kayıtları dört etiketten biriyle başlar: `" STR"`, `" RTS"`, `"STRW"`, `"WRTS"`.
Son ikisi metinden sonra ayrıca bir **extra blok** (`extraLen:u32` + ASCII veri) taşır.

Kod yalnızca `"STRW"`i kontrol ediyordu:

```csharp
bool hasExtra = sMagic == "STRW";   // "WRTS" atlanıyor
```

`generals.csf` içinde iki `WRTS` kaydı var; ilki **4177. etiket** olan
`DIALOGEVENT:MisGLA02Chatter18Subtitle` (extra = `"superiorite"`).
Extra blok okunmayınca akış 15 bayt kayıyor, sonraki `" LBL"` kontrolü tutmuyor ve döngü
`break` ile çıkıyor — **kalan 2245 etiket sessizce siliniyordu.**

| | Etiket sayısı | CSF boyutu |
|---|---|---|
| Vanilla | 6422 | 928.775 B |
| Bozuk çıktı | **4177** | 508.476 B |

Etkilenen dosyalar:

- `Launcher/Services/BigCsfWriter.cs` → `ApplyAllOverrides` (yazma yolu — dosyayı bozan)
- `Launcher/Services/BigCsfReader.cs` → `ParseCsf` (UI etiket listesi)
- `CommandCardTool/Services/BigCsfReader.cs` → `ParseCsf` (aynı)

> `CommandCardTool/Services/CsfCodec.cs` her iki etiketi de doğru işliyordu; bu yüzden
> hata yalnızca `BigCsfReader`/`BigCsfWriter` kullanan yollarda görünüyordu.

## 2. Neden tam da Toxin tünel ve Nuke?

Kesme noktası 4177. Genel (general) varyantı olan CONTROLBAR anahtarlarının neredeyse
tamamı CSF'in **sonunda** duruyor:

| Anahtar | Vanilla indeksi | Bozuk çıktıda |
|---|---|---|
| `CONTROLBAR:Chem_ConstructGLATunnelNetwork` | 6233 | yok |
| `CONTROLBAR:Chem_ToolTipGLABuildTunnelNetwork` | 6235 | yok |
| `CONTROLBAR:Nuke_ToolTipChinaBuildHelix` | 4177 | yok |
| `CONTROLBAR:AirF_ToolTipUSAScienceCarpetBomb` | 6421 | yok |

Kesilen bölgede **81 CONTROLBAR** anahtarı var (`Chem_`, `Nuke_`, `Boss_`, `Infa_`,
`SupW_`, `AirF_` varyantları). Oyun anahtarı bulamayınca `MISSING: '…'` yazıyor ve
CSF'teki `&` bağlantısı kurulamadığı için **kısayol tuşu çalışmıyor.**
Normal komut kartı anahtarları (indeks < 4177) sağlam kaldığı için sorun
"sadece genel skillerde" gibi görünüyordu.

## 3. İkinci hata — kısayol harfi kelimeyi bozuyordu

`SetHotkeyCharInLabel`, mevcut `&`den **sonraki harfin üzerine yazıyordu**:

```
&Barracks       + S  →  &Sarracks
S&upply Center  + D  →  S&Dpply Center
Wor&ker         + R  →  Wor&Rer
Tunnel &Network + V  →  Tunnel &Retwork
```

Bozuk çıktıdaki 22 değişikliğin **22'si de** bu şekilde bozulmuştu — "yazılar bozuk"
şikayetinin kaynağı bu.

## 4. Doğru kısayol biçimi

Çalışan referans, gömülü `&` yerine **sona parantezli sonek** kullanıyor:

```
controlbar:chem_constructglatunnelnetwork  =  Toxin Network (&V)
controlbar:constructglatunnelnetwork       =  Tunnel Network (&V)
controlbar:constructchinavehiclenukelauncher = Nuke Cannon (&X)
```

Referanstaki 328 `&` içeren CONTROLBAR etiketinin 277'si bu biçimde. Tooltip
anahtarlarına `&` **eklenmiyor**.

## 5. Yapılan düzeltmeler (2026-07-25)

| Dosya | Değişiklik |
|-------|------------|
| `Launcher/Services/BigCsfWriter.cs` | `WRTS` + `STRW` extra bloğu okunuyor/yazılıyor; geçersiz etikette sessiz kesme yerine `null` dönülüyor; etiket sayısı doğrulanıyor |
| `Launcher/Services/BigCsfWriter.cs` | `RebuildAll` çıktısı kaynaktan az etiket içeriyorsa yazılmıyor; kaynak = çıktı ise iptal |
| `Launcher/Services/BigCsfReader.cs` | `WRTS` extra bloğu atlanıyor |
| `CommandCardTool/Services/BigCsfReader.cs` | aynı |
| `*/Services/CommandCardHotkeyService.cs` | `SetHotkeyCharInLabel` artık harfin üzerine yazmıyor; gömülü `&` yalnızca zaten doğru harfi gösteriyorsa korunuyor, aksi halde ` (&X)` soneki |
| `CommandCardTool/Services/BigCsfWriter.cs` | Önceki override yalnızca **sağlamsa** (etiket sayısı ≥ vanilla) taban alınıyor; boyut sınırı yerine içerik kontrolü — daha önce boyanmış atlaslar artık yeniden kayıtta korunuyor |

## 5b. Next.js projesindeki eksikler (aynı tarihte düzeltildi)

Downloads'taki bozuk dosyayı **C# üretti**; web codec'i `WRTS`i doğru işliyordu. Yine de
`lib/csf-utils.ts` içinde kayıplar vardı:

| Sorun | Etki | Düzeltme |
|---|---|---|
| `parseCsf` etiket başına değil **string başına** öğe üretiyordu | String'i olmayan etiket (`TOOLTIP:InvalidGameVersion`) her indirmede siliniyordu; çok string'li etiket aynı id ile ikiye bölünüyordu | Etiket başına tek öğe; fazladan string'ler `rest[]`, boş etiket `empty` ile korunuyor |
| `buildCsf` header'ı sabit yazıyordu (`version=3, reserved=0, language=0`) | İngilizce dışı CSF'in dil alanı sıfırlanıyordu | `parseCsfHeader` ile okunup geri yazılıyor |
| `numStrings` alanına etiket sayısı yazılıyordu | Header gerçek string sayısını yansıtmıyordu | Yazılan string'ler sayılıyor |
| `parseCsf` desync'te sessizce kısa liste dönüyordu | C#'taki felaketin aynısı: eksik CSF indirilir | Kayıt öncesi `buildCsfChecked` header'daki etiket sayısıyla karşılaştırıyor, eksikse kaydı iptal edip uyarıyor |

Sonuç: vanilla `generals.csf` artık web'de de **bayt bayt aynı** round-trip yapıyor
(önce 928.775 → 928.737 B, 1 etiket kayıp).

Web tarafında kısayol metnini kullanıcı elle yazdığı için §3'teki harf bozma hatası yok.

## 6. Eski dokümandaki hatalı tespitler

| Eski iddia | Gerçek |
|---|---|
| «Vanilla CSF'te `Chem_ConstructGLATunnelNetwork` **yok**» | **Var** — indeks 6233, değeri `Toxin &Network`. Eski analiz kesilmiş CSF'i okuduğu için göremedi. |
| «Oyun sondaki `(&X)` biçimini kabul etmez» | **Kabul ediyor** — çalışan referansın tamamı bu biçimde. |

## 7. Doğrulama

```
Launcher ApplyAllOverrides(vanilla, {})   928.775 B → 928.775 B, bayt bayt aynı
                          (önce)          928.775 B → 508.468 B
BigCsfReader.ReadFromBig(vanilla)         6421 etiket  (önce: 4176)
CONTROLBAR:Chem_ConstructGLATunnelNetwork "Toxin Network (&V)"  == referans
&Barracks + S                             "Barracks (&S)"  (önce: "&Sarracks")
&Barracks + B                             "&Barracks"      (değişmeden korunur)
```

Yeniden kayıt senaryosu (CommandCardTool, iki ardışık kayıt):

```
kayıt#1  entries=2  csfLabels=6422  atlases=[SUUserInterface512_004.tga]
kayıt#2  entries=3  csfLabels=6422  atlases=[SUUserInterface512_004.tga, SNUserInterface512_003.tga]
         her iki kayıttaki metin değişiklikleri de korunuyor
```

## 8. Mevcut bozuk dosyalar

Düzeltilmiş EXE eski çıktıyı onarmaz — bozuk override'lar **silinmeli**:

```
…\Command & Conquer Generals - Zero Hour\!EnglishZH.big
…\Command & Conquer Generals - Zero Hour\!EnglishZH.big.bak
C:\Users\MSI\Downloads\!EnglishZH.big
```

Silindikten sonra düzenleme yeniden yapılır; taban her zaman vanilla `EnglishZH.big`.

## 9. BIG yükleme sırası (değişmedi)

Oyun klasördeki `.big` dosyalarını alfabetik yükler, **ilk gelen kazanır**:

```
!EnglishZH.big                     ← araç çıktısı (CSF + boyanmış atlaslar)
!HotkeysLeikezeIndicatorsZH.big    ← atlas + Data\INI\MappedImages\… + CommandButton.ini
!HotkeysLeikezeZH.big              ← hazır profil CSF'i
EnglishZH.big                      ← vanilla
```

`!EnglishZH.big`, `!Hotkeys…` dosyalarından önce geldiği için araç çıktısı her zaman
üstte kalır. `GameBigStack.IsHotkeyProfileBig` sayesinde `!HotkeysLeikeze*` dosyaları
«Current CSF» birleştirmesine katılmaz.
