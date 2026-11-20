# AI Usage Disclosure / AI-használat

[English](#english) · [Magyar](#magyar) · [README](README.md)

## English

### Development tools and contribution

I used **OpenAI ChatGPT**, including coding and file-editing assistance, throughout development. Its contribution included substantial code generation and revision, not just autocomplete.

| Area | AI assistance |
| --- | --- |
| Interface and application code | WPF layout, C#/XAML implementation, audio integration, classification and ticket handling |
| Debugging | Interpreting compiler errors and screenshots; correcting runtime paths and missing-asset handling |
| Training data | Creating Hungarian fault examples, synonyms and alternative wording |
| Model experiments | Training, model-selection and evaluation code |
| Documentation | English and Hungarian project descriptions and instructions |

The use case came from my scooter-maintenance experience. I selected the workflow, integrated the changes in Visual Studio, ran model-training experiments and tested the application locally. I reported failures and requested revisions when generated code or behaviour was unsuitable. Manual checks are not exhaustive testing.

### AI in the application

- **Whisper.cpp** runs speech-to-text locally.
- **ML.NET** classifies descriptions into predefined fault codes. The selected configuration uses SDCA Maximum Entropy with word and character features.
- A **CSV catalogue** maps the predicted fault code to a category and possible repair. This is retrieval, not generated repair advice.

ChatGPT is a development tool, not a runtime dependency of this workflow. The described transcription and classification do not call the ChatGPT API.

### Data and evaluation

Training data includes AI-assisted examples and paraphrases, rather than exclusively independently collected workshop incidents.

Model selection used a validation set; a separate test set was evaluated afterwards. Identical normalised texts were kept together, but related paraphrase families were not fully grouped. Similar wording across splits may therefore make the evaluation easier than real-world use.

The selected configuration achieved **66.32% test MacroAccuracy** and **71.17% test MicroAccuracy**. The original configuration achieved **44.82% MacroAccuracy** on the same test split. These measure fault classification only. The final model was then retrained on all available data and has no new independent test score.

Repair suggestions require technician review. Further evaluation should use independently collected descriptions and audio recorded in workshop conditions.

---

## Magyar

### Fejlesztési eszközök és hozzájárulás

A fejlesztés során **OpenAI ChatGPT-t** használtam, kódolási és fájlszerkesztési segítséggel együtt. A hozzájárulása jelentős kódgenerálást és átdolgozást is tartalmazott, nem csupán automatikus kódkiegészítést.

| Terület | AI-segítség |
| --- | --- |
| Felület és alkalmazáskód | WPF-elrendezés, C#/XAML-megvalósítás, hangfeldolgozás bekötése, besorolás és hibajegykezelés |
| Hibakeresés | Fordítási hibák és képernyőképek értelmezése; futási útvonalak és hiányzó fájlok kezelésének javítása |
| Tanítóadatok | Magyar hibaleírások, szinonimák és eltérő megfogalmazások készítése |
| Modellkísérletek | Tanító-, modellválasztó és kiértékelő kód |
| Dokumentáció | Angol és magyar projektleírások és útmutatók |

A felhasználási eset a rollerszervizelésben szerzett tapasztalataimból származik. A munkafolyamatot én választottam ki, a módosításokat Visual Studióban építettem be, lefuttattam a tanítási kísérleteket és helyben teszteltem az alkalmazást. Jeleztem a hibákat, és módosítást kértem, ha a generált kód vagy a működés nem volt megfelelő. A kézi ellenőrzések nem jelentenek teljeskörű tesztelést.

### AI az alkalmazásban

- A **Whisper.cpp** helyben végzi a beszéd szöveggé alakítását.
- Az **ML.NET** előre meghatározott hibakódokba sorolja a leírásokat. A kiválasztott konfiguráció SDCA Maximum Entropy algoritmust, valamint szó- és karakterjellemzőket használ.
- Egy **CSV-katalógus** kapcsolja a becsült hibakódot kategóriához és lehetséges javításhoz. Ez kikeresés, nem generált javítási tanács.

A ChatGPT fejlesztési eszköz, nem a munkafolyamat futási függősége. A bemutatott átírás és besorolás nem hívja a ChatGPT API-ját.

### Adatok és kiértékelés

A tanítóadatok AI-val támogatott példákat és átfogalmazásokat is tartalmaznak, nem kizárólag függetlenül gyűjtött műhelyeseteket.

A modellválasztás validációs készleten történt, majd külön tesztkészletet értékeltünk. Azonos normalizált szövegek egy csoportban maradtak, de a rokon átfogalmazásokat nem csoportosítottuk teljeskörűen. A felosztások közötti hasonló megfogalmazások ezért könnyebbé tehetik a kiértékelést a valódi használatnál.

A kiválasztott konfiguráció **66,32% teszt MacroAccuracy** és **71,17% teszt MicroAccuracy** értéket ért el. Az eredeti konfiguráció ugyanazon tesztfelosztáson **44,82% MacroAccuracy** eredményt adott. Ezek csak a hibabesorolást mérik. A végleges modellt ezután az összes adaton újratanítottuk; ehhez nincs új független teszteredmény.

A javítási javaslatokat technikusnak kell ellenőriznie. A további kiértékeléshez függetlenül gyűjtött leírások és műhelykörülmények között rögzített hangfelvételek szükségesek.
