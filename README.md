## Pipeline CI/CD

O projeto utiliza GitHub Actions para um pipeline CI/CD que inclui:

1. **Checkout do Código**: Utiliza `actions/checkout@v2` para clonar o código do repositório.
2. **Setup do Ambiente**: Configura o .NET Core usando `actions/setup-dotnet@v1`.
3. **Build do Projeto**: Compila o projeto com `dotnet build` O flag `--no-restore` é usado para evitar restaurar dependências novamente,
uma vez que já foi feito na etapa anterior..
4. **Testes Automatizados**: Executa testes com `dotnet test` e coleta cobertura de código, usando Codecov.
5. **Análise de Qualidade**: Utiliza o SonarCloud para análise de qualidade do código.
6. **Publicação de Artefatos**: Publica artefatos de build para uma pasta especificada com `dotnet publish`
e utiliza `action/upload-artifac@v4`.

Para mais detalhes, veja os arquivos `.github/workflows/jellyfin-ci.yml`
                                     `.github/workflows/buildSonar.yml`
                                     `.github/workflows/buildCodecov.yml`.

#SonarCloud: 
O SonarCloud é uma solução SaaS (Software as a Service) hospedada pela
SonarSource na AWS. Ideal para equipes que preferem um ambiente totalmente
baseado em nuvem, o SonarCloud oferece integração fácil com plataformas populares
como GitHub, Azure DevOps, Bitbucket e GitLab. Ele também apresenta análise
automática para muitos idiomas populares, facilitando a implementação rápida e a
obtenção de métricas de qualidade de código sem a necessidade de configuração
extensa, e é uma ferramenta com opção gratuita para estudos. E por esses motivos, eu
a escolhi.

#Codecov com coverlet:
A ferramenta Codecov mede a cobertura de teste de código, identificando quais métodos e instruções não são testados. Os resultados podem ser usados para determinar onde escrever testes, melhorando a qualidade do código. O codecov oferece integração, feedback em pull request, integração com Sentry, gratuito para projetos open source, status check, e estadisponível para os repositórios de origem GitHub, GitHub Enterprise Server e Bitbucket. Por esses motivos essa ferramenta foi escolhida.
