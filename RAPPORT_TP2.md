# Rapport TP2 - Mini Application AR avec IA

## 1. Contexte
Ce TP demande une mini application AR qui detecte un element reel, declenche une logique intelligente, et affiche un contenu dynamique en realite augmentee.

Technologie choisie:
- Vuforia (Image Target)

## 2. Objectif Realise
Application realisee: Modern AR AI Maintenance Assistant

Scenario:
1. L'utilisateur pointe la camera vers une image cible
2. Vuforia detecte l'image
3. L'application lance une analyse IA (Gemini si disponible, sinon fallback local)
4. L'application affiche:
   - niveau de risque
   - confiance du diagnostic
   - constats techniques
   - action recommandee
  - historique des derniers scans
  - KPI cards 3D autour du target (temperature, vibration, pression)
  - export de rapport depuis l'interface
  - capture screenshot automatique lors de l'export
  - interface bilingue FR/EN

## 3. Conformite au Cahier de Charge
- Initialisation AR: OK (Vuforia)
- Detection image target: OK
- Placement contenu AR: OK (UI/infos ancrees au flux AR)
- Interaction utilisateur: OK (scan + tap/click panel cycle)
- Integration IA: OK (Gemini API + fallback simulation)
- Affichage dynamique: OK

## 4. Architecture Technique
Scripts principaux:
- Assets/AIAgentController.cs
  - Pipeline d'analyse en 3 etapes
  - Integration Gemini REST
  - Parsing de reponse IA
  - Fallback local si erreur/reseau/cle absente
  - Interface multi-panels
  - Generation des KPI cards 3D
  - Export in-app du rapport de diagnostic
  - Capture screenshot automatique sur export
  - Animation d'apparition des KPI cards
  - Localisation FR/EN des textes UI

- Assets/CameraStartupDiagnostics.cs
  - Verification webcam en Play mode
  - Gestion autorisation camera (Android)
  - Alerte si webcam virtuelle prioritaire (OBS)

## 5. Flux Fonctionnel
- OnTargetFound:
  - TriggerAIAnalysis()
  - Etape 1: collecte signaux
  - Etape 2: modele anomalie
  - Etape 3: consultation Gemini
  - Affichage panel Summary

- Tap/click utilisateur:
  - Summary -> Action -> History -> Summary

- Bouton Export Report:
  - sauvegarde d'un rapport texte dans `Application.persistentDataPath/Reports`
  - sauvegarde d'une capture ecran `.png` avec le meme timestamp
  - trace console du chemin complet

- OnTargetLost:
  - ResetState()
  - Retour a l'etat idle

## 6. Integration Gemini
Endpoint utilise:
- https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent

Strategie:
- Prompt structure orientee maintenance
- Reponse attendue en JSON compact
- Parsing JSON -> DiagnosticReport
- Fallback automatique si parsing/HTTP echoue

## 7. Stabilite et Fiabilite
Ameliorations appliquees:
- Anti-null references pour UI
- Anti-double coroutine
- Cooldown anti retrigger spam
- Camera diagnostics detaillees
- Fallback de secours toujours disponible

## 8. Resultats
Resultat final:
- Application AR AI moderne et demonstrable
- Fonctionne sans cle API (simulation)
- Fonctionne avec cle API (Gemini live)
- Interaction fluide et claire pour demo
- KPI cards 3D contextuelles autour du target
- Export rapport in-app operationnel
- Screenshot automatique associe au rapport exporte
- Animation KPI pour une presentation plus dynamique
- Bascule FR/EN pour la soutenance
- Theme UI renforce pour soutenance (look demo-ready)

## 9. Limites Actuelles
- Export actuel en format texte (pas PDF natif)
- Dependance reseau pour Gemini live
- Qualite reponse IA variable selon prompt
- Detection d'objets arbitraires (ex: tout type de legumes) non couverte par le seul mode ImageTarget

## 10. Ameliorations Futures
- Export rapport PDF in-app
- Historique persistent (fichier local/cloud)
- Mode multi-langue et TTS
- capture screenshot automatique jointe au rapport

## 11. Procedure APK (resume)
1. Switch platform Android
2. Verifier scenes dans Build Settings
3. Config Player Settings
4. Build -> generer APK
5. Recuperer fichier .apk dans le dossier choisi

## 12. Conclusion
Le TP2 est realise avec une vraie proposition moderne AR + IA:
- Detection visuelle AR
- Intelligence artificielle exploitable
- Affichage dynamique actionnable
- Structure propre pour evolutions futures
