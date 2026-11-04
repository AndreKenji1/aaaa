# language: pt
@bdd
Funcionalidade: Monitoramento do consumo de água
  Como administrador do HydroGuard
  Quero cadastrar medidores e acompanhar os consumos
  Para identificar possíveis vazamentos

  Contexto:
    Dado que estou autenticado como administrador

  Cenário: Cadastrar um medidor e encontrá-lo na listagem
    Quando cadastro um medidor com serial "HG-BDD-001"
    Então o medidor é criado e aparece na listagem com o mesmo serial

  Cenário: Impedir o cadastro de um serial duplicado
    Dado que já existe o medidor com serial "HGTEST001"
    Quando tento cadastrar outro medidor com serial "HGTEST001"
    Então o cadastro é recusado sem duplicar o medidor

  Cenário: Registrar consumo de um medidor existente
    Dado que existe um medidor para monitoramento
    Quando registro 350 litros de consumo
    Então o consumo é criado com data automática e aparece no histórico desse medidor

  Cenário: Recusar um consumo negativo
    Dado que existe um medidor para monitoramento
    Quando tento registrar um consumo de -10 litros
    Então o consumo é recusado por validação e o histórico permanece vazio

  Cenário: Detectar um aumento de consumo e incluir o alerta no relatório
    Dado que existe um medidor para monitoramento
    E que esse medidor possui um consumo anterior de 100 litros
    Quando registro 300 litros de consumo
    E consulto os possíveis vazamentos
    Então recebo um alerta de severidade "Alta" para esse medidor
    E esse alerta crítico aparece no relatório gerencial

  Cenário: Não gerar alerta quando o consumo não ultrapassa o limite
    Dado que existe um medidor para monitoramento
    E que esse medidor possui um consumo anterior de 100 litros
    Quando registro 140 litros de consumo
    E consulto os possíveis vazamentos
    Então não recebo alertas de vazamento
