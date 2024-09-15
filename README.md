## Pipeline CI/CD

O projeto utiliza GitHub Actions para um pipeline CI/CD que inclui:

1. **Checkout do Código**: Utiliza `actions/checkout@v2` para clonar o código do repositório.
2. **Setup do Ambiente**: Configura o .NET Core usando `actions/setup-dotnet@v1`.
3. **Build do Projeto**: Compila o projeto com `dotnet build`.
4. **Testes Automatizados**: Executa testes com `dotnet test` e coleta cobertura de código.
5. **Análise de Qualidade**: Utiliza o SonarCloud para análise de qualidade do código.
6. **Publicação de Artefatos**: Publica artefatos de build.
7. **Deploy Opcional**: Cria e publica uma imagem Docker do Jellyfin.

Para mais detalhes, veja os arquivos `.github/workflows/jellyfin-ci.yml`
                                     `.github/workflows/buildSonar.yml`
                                     `.github/workflows/buildCodecov.yml`.
