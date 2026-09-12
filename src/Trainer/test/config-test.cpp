// Prueft das Lesen der Konfiguration ohne Spiel.
//
// Die Konfiguration haengt bewusst an keinem Plattformheader - deshalb laesst
// sie sich hier vollstaendig durchspielen. Das lohnt besonders, weil die Fehler
// hier stumm sind: eine falsch geschriebene Taste aeussert sich im Spiel als
// "die Taste tut nichts", und das sucht man dann im Spiel statt in der Datei.

#include <cstdio>
#include <string>
#include <vector>

#include "../core/Config.h"

namespace
{
    int g_passed = 0;
    int g_failed = 0;

    void Check(const bool condition, const char* label)
    {
        if (condition)
        {
            std::printf("  PASS  %s\n", label);
            ++g_passed;
        }
        else
        {
            std::printf("  FAIL  %s\n", label);
            ++g_failed;
        }
    }

    bool Has(const std::vector<int>& codes, const int code)
    {
        for (const int c : codes)
        {
            if (c == code)
            {
                return true;
            }
        }

        return false;
    }

    bool Mentions(const std::vector<std::string>& lines, const std::string& fragment)
    {
        for (const std::string& line : lines)
        {
            if (line.find(fragment) != std::string::npos)
            {
                return true;
            }
        }

        return false;
    }
}

int main()
{
    std::printf("\n== Tastennamen ==\n");

    Check(mliv::KeyCodeFromName("F7") == 0x76, "F7 wird erkannt");
    Check(mliv::KeyCodeFromName("f7") == 0x76, "Kleinschreibung wird erkannt");
    Check(mliv::KeyCodeFromName("  F7  ") == 0x76, "Leerzeichen stoeren nicht");
    Check(mliv::KeyCodeFromName("NUM8") == 0x68, "NUM8 wird erkannt");
    Check(mliv::KeyCodeFromName("NUMPAD8") == 0x68, "der englische Zweitname auch");
    Check(mliv::KeyCodeFromName("HOCH") == 0x26, "HOCH wird erkannt");
    Check(mliv::KeyCodeFromName("UP") == 0x26, "UP ergibt dieselbe Taste");
    Check(mliv::KeyCodeFromName("K") == 'K', "einzelne Buchstaben sind ihr eigener Code");
    Check(mliv::KeyCodeFromName("k") == 'K', "auch klein geschrieben");
    Check(mliv::KeyCodeFromName("5") == '5', "einzelne Ziffern ebenso");

    // Der wichtigste Fall: was nicht erkannt wird, muss als 0 zurueckkommen,
    // damit der Aufrufer seine Vorgabe behaelt statt eine Zufallstaste zu binden.
    Check(mliv::KeyCodeFromName("NUM 8") == 0, "NUM 8 mit Leerzeichen wird nicht erkannt");
    Check(mliv::KeyCodeFromName("Wurstbrot") == 0, "Unsinn wird nicht erkannt");
    Check(mliv::KeyCodeFromName("") == 0, "leerer Name wird nicht erkannt");

    std::printf("\n== Lesen ==\n");
    {
        mliv::Config config;
        config.parse(
            "# ein Kommentar\n"
            "; noch einer\n"
            "\n"
            "[Tasten]\n"
            "Menue   = F8\n"
            "Hoch    = NUM8, HOCH\n"
            "\n"
            "[Menue]\n"
            "Links   = 0.05\n"
            "\n"
            "[Protokoll]\n"
            "Aktiv = nein\n");

        Check(Has(config.keys("Menue"), 0x77), "Menuetaste kommt aus der Datei");
        Check(config.keys("Hoch").size() == 2, "zwei Tasten fuer eine Aktion");
        Check(Has(config.keys("Hoch"), 0x68) && Has(config.keys("Hoch"), 0x26),
              "beide sind die richtigen");
        Check(config.keys("Runter").empty(), "was nicht dasteht, bleibt leer");

        Check(config.number("Menue.Links", 99.0f) > 0.049f &&
              config.number("Menue.Links", 99.0f) < 0.051f, "Zahl wird gelesen");
        Check(config.number("Menue.Oben", 0.12f) > 0.119f, "fehlende Zahl faellt auf die Vorgabe");

        Check(config.flag("Protokoll.Aktiv", true) == false, "nein wird als falsch gelesen");
        Check(config.flag("Protokoll.Gibtsnicht", true), "fehlender Schalter faellt auf die Vorgabe");

        Check(config.problems().empty(), "an einer sauberen Datei gibt es nichts zu melden");
    }

    std::printf("\n== Gross- und Kleinschreibung ==\n");
    {
        mliv::Config config;
        config.parse("[TASTEN]\nMENUE = F9\n");

        Check(Has(config.keys("menue"), 0x78), "Abschnitt und Name sind schreibungsunabhaengig");
    }

    std::printf("\n== Was schiefgehen kann ==\n");
    {
        mliv::Config config;
        config.parse(
            "[Tasten]\n"
            "Menue = Wurstbrot\n"
            "Hoch = NUM 8\n"
            "Dies ist keine Zuweisung\n"
            "= ohne Namen\n"
            "[Menue]\n"
            "Links = 0,5\n"
            "Breite = weit\n"
            "[Protokoll]\n"
            "Aktiv = vielleicht\n");

        Check(config.keys("Menue").empty(), "unbekannte Taste wird nicht gebunden");
        Check(config.keys("Hoch").empty(), "die mit dem Leerzeichen auch nicht");

        // Beide Meldungen liegen vor, ohne dass jemand die betroffene Aktion
        // abgefragt haette. Wuerde erst beim Abfragen geprueft, bliebe der
        // Tippfehler in einer Aktion, nach der niemand fragt, fuer immer stumm.
        Check(Mentions(config.problems(), "Wurstbrot"), "und wird gemeldet");
        Check(Mentions(config.problems(), "NUM 8"), "auch die mit dem Leerzeichen");
        Check(Mentions(config.problems(), "Gleichheitszeichen"), "Zeile ohne = wird gemeldet");
        Check(Mentions(config.problems(), "leerer Name"), "Zuweisung ohne Namen wird gemeldet");

        // Das Komma als Dezimaltrenner ist der wahrscheinlichste Tippfehler auf
        // einem deutschen System. Ohne Pruefung ergaebe "0,5" stillschweigend 0,
        // und das Menue klebte am linken Rand.
        Check(config.number("Menue.Links", 0.025f) > 0.024f &&
              config.number("Menue.Links", 0.025f) < 0.026f, "0,5 mit Komma wird nicht als 0 gelesen");
        Check(Mentions(config.problems(), "Zahl mit Punkt"), "und der Grund wird genannt");

        Check(config.number("Menue.Breite", 0.235f) > 0.234f, "Text statt Zahl faellt auf die Vorgabe");
        Check(config.flag("Protokoll.Aktiv", true), "unklarer Schalter faellt auf die Vorgabe");
        Check(Mentions(config.problems(), "ja oder nein"), "und sagt, was erwartet wird");
    }

    std::printf("\n== Doppelte Zeilen ==\n");
    {
        mliv::Config config;
        config.parse("[Tasten]\nMenue = F7\nMenue = F9\n[Menue]\nLinks = 0.1\nLinks = 0.2\n");

        Check(Has(config.keys("Menue"), 0x78) && config.keys("Menue").size() == 1,
              "bei Tasten gewinnt die untere Zeile");
        Check(config.number("Menue.Links", 0.0f) > 0.19f, "bei Zahlen ebenso");
    }

    std::printf("\n== Die mitgelieferte Vorlage ==\n");
    {
        // Die Vorlage ist das, was jeder Nutzer als Erstes sieht. Waere darin ein
        // Tippfehler, faende ihn niemand - er sieht ja aus wie Absicht.
        mliv::Config config;
        config.parse(mliv::Config::DefaultText());

        Check(config.problems().empty(), "die Vorlage liest sich ohne Beanstandung");
        Check(Has(config.keys("Menue"), 0x76), "und bindet F7 auf das Menue");
        Check(config.keys("Hoch").size() == 2, "Hoch hat Zehnerblock und Pfeiltaste");
        Check(config.keys("Runter").size() == 2, "Runter auch");
        Check(config.keys("Links").size() == 2, "Links auch");
        Check(config.keys("Rechts").size() == 2, "Rechts auch");
        Check(config.keys("Waehlen").size() == 2, "Waehlen auch");
        Check(config.keys("Zurueck").size() == 2, "Zurueck auch");
        Check(config.flag("Protokoll.Aktiv", false), "das Protokoll ist voreingestellt an");
    }

    std::printf("\n%s\n", std::string(46, '=').c_str());
    std::printf(" %d bestanden, %d fehlgeschlagen\n", g_passed, g_failed);
    std::printf("%s\n\n", std::string(46, '=').c_str());

    return g_failed == 0 ? 0 : 1;
}
