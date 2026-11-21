# Game_QA_Automated
## Windows builds and releases

GitHub Actions builds GameQA and runs its image-matching self-test on pushes and pull requests. Download the Windows ZIP from the run's Artifacts section. The self-test does not launch a game or exercise desktop recording.

To publish a GitHub Release, push a version tag such as v0.1.0 on a tested commit. GitHub publishes the Windows ZIP only after the build and self-test pass.

See [GameQA usage](GameQA/README.md) for instructions.
