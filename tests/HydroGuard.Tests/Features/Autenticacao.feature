# language: pt
@bdd
Funcionalidade: Autenticação do HydroGuard
  Como administrador do monitoramento de água
  Quero entrar com minhas credenciais
  Para acessar as funcionalidades protegidas

  Cenário: Entrar com credenciais válidas
    Quando faço login com usuário "admin" e senha "admin123"
    Então recebo um token JWT válido que permite consultar os medidores

  Cenário: Recusar uma senha incorreta
    Quando faço login com usuário "admin" e senha "senha-incorreta"
    Então o acesso é negado com a mensagem "Credenciais inválidas" e sem token
