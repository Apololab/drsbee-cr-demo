﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DrsBee.API;

namespace Demo
{
    class MainClass
    {
        static BackendConfiguration CONFIG = BackendConfiguration.DEV_PA;
        const string TEST_PHYSICIAN_IDENTIFICATION = "o2619142";
        const string TEST_PHYSICIAN_IDENTIFICATION_TYPE_CODE = "2";
        static float LONGITUDE = CONFIG.DefaultLongitude;
        static float LATITUDE = CONFIG.DefaultLatitude;

        const string PATIENT_IDENTIFICATION = "1-100-12";
        const string CEDULA_PANAMA_TYPE = "1";

        const string API_KEY_RESOURCE = "apiclient.key"; // El archivo esta incluído en el proyecto como resource, en el directorio "Resources"
        const string API_KEY_ACCOUNT = "apiuser@test.com";

        static InfoWebService infoServices = new InfoWebService(CONFIG);
        static UserWebService userWebService = new UserWebService(CONFIG);
        static APIWebServices apiWebServices = new APIWebServices(CONFIG);
        static PatientWebService patientWebService = new PatientWebService(CONFIG);
        static EncounterWebService encounterWebService = new EncounterWebService(CONFIG);
        static DrugWebService drugWebService = new DrugWebService(CONFIG);
        static CatalogWebService catalogWebService = new CatalogWebService(CONFIG);

        public static void Main(string[] args)
        {
            // Inicializamos el token para los servicios de API
            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(API_KEY_RESOURCE)) {
                DrsBeeAuth.InitApi(API_KEY_ACCOUNT, resourceStream);
            }

            //Revisa la conexión al ambiente y obtiene info basica
            Console.WriteLine("-------- Conectando a ambiente ------");
            var environment = infoServices.getBackendEnvironmentAsync().Result;
            Console.WriteLine("-> "+ environment.applicationEnvironment);
            Console.WriteLine("-> Vademecum:" + environment.defaultVademecumDescription+"(ID"+ environment.defaultVademecumId +")");

            //Obtenemos los catalogos necesarios
            List<TimeUnit> timeUnits = catalogWebService.getTimeUnitsAsync().Result;
            List<PrescriptionAbbreviature> prescriptionAbbreviatures = catalogWebService.getPrescriptionAbbreviaturesAsync().Result;

            //Se hace login
            Console.WriteLine("-------- Login con médico de pruebas mediante API ------");
            var login = apiWebServices.loginAsHealthProfessional(TEST_PHYSICIAN_IDENTIFICATION, TEST_PHYSICIAN_IDENTIFICATION_TYPE_CODE).Result;
            Console.WriteLine("-> " + login.userType);

            //Una vez obteniendo el tipo de usuario logeado, se procede a obtener sus datos
            Console.WriteLine("-------- Login con médico de prueba ------");
            var physician = userWebService.getPhysicianLoginAsync().Result;
            Console.WriteLine("-> Cedula " + physician.identification);
            Console.WriteLine("-> Nombre " + physician.firstName+"-"+ physician.lastName);

            //Obtenemos cuantas prescripciones tiene restantes
            var prescriptions = userWebService.getHealthProfessionalRemainingPrescriptionsAsync().Result;
            Console.WriteLine("-> Prescripciones restantes " + prescriptions.count);


            //Se busca un paciente por nombre
            Console.WriteLine("-------- Buscando pacientes por nombre ------");
            var pacientes = patientWebService.searchPatientsAsync(criteria: "juan",includeUnregistered: true, limit: 50).Result; 
            Console.WriteLine("-> Pacientes encontrados por nombre " + pacientes.Count);

            Console.WriteLine("-------- Buscando paciente específico por identificación ------");

            var pacienteRegistrado = patientWebService.getBasicPatientByIdentificationAsync( PATIENT_IDENTIFICATION,CEDULA_PANAMA_TYPE,false ).Result;

            CreatedEncounter encounter;
            // Procedemos a crear una cita, ya sea con un paciente registrado o con uno nuevo para registrar
            if (pacienteRegistrado != null)
            {
                Console.WriteLine("-------- Iniciando cita con paciente ya registrado ------");
                encounter = encounterWebService.beginEncounterPatientAsync(pacienteRegistrado.id, "cita de prueba").Result;
            }
            else
            {
                Console.WriteLine("-------- Iniciando cita con paciente nuevo ------");
                encounter = encounterWebService.beginEncounterNewPatientAsync(
                    identification: PATIENT_IDENTIFICATION, identificationTypeCode: CEDULA_PANAMA_TYPE,
                    firstName: "paciente", lastName: "prueba", 
                    phoneNumber: "88888888", 
                    birthdateDay: 1, birthdateMonth: 1, birthdateYear:1999
                    ,reason: "cita de prueba").Result;
            }


            Console.WriteLine("-------- Buscando medicamentos ------");
            var drugSearchResult = drugWebService.findDrugsByFilterAsync("Aceta").Result;
            Console.WriteLine("-> Encontrados por marca " + drugSearchResult.drugsByName.Count);
            Console.WriteLine("-> Encontrados por principio activo " + drugSearchResult.drugsByActiveIngredient.Count);

            Drug drugToAdd = drugSearchResult.drugsByName[0];

            // Muchos medicamentos traen su dosificación usual, si la trae vamos a usarla
            var suggestedDosage = drugSearchResult.dosageSuggestionByDrugId.ContainsKey(drugToAdd.id) ?
                drugSearchResult.dosageSuggestionByDrugId[drugToAdd.id] : null;

            PrescriptionDrug prescriptionDrug = new PrescriptionDrug();
            prescriptionDrug.drugId = drugToAdd.id;

            prescriptionDrug.dose = suggestedDosage != null && suggestedDosage.dose != null ? suggestedDosage.dose.Value : 1;
            prescriptionDrug.administrationRouteId = drugToAdd.administrationRouteById.Keys.First() ; // Como ejemplo , utilizamos la primera via de administracion
            prescriptionDrug.duration = suggestedDosage != null && suggestedDosage.duration != null ? suggestedDosage.duration.Value : 1;
            prescriptionDrug.duration = suggestedDosage != null && suggestedDosage.duration != null ? suggestedDosage.duration.Value : 1;
            prescriptionDrug.durationTimeUnitCode = suggestedDosage != null && !string.IsNullOrEmpty(suggestedDosage.durationTimeUnitCode) ? suggestedDosage.durationTimeUnitCode: timeUnits[0].code;

            // Para la frecuencia del consumo utilizamos codigo predefinidos que establecen el horario, por ejemplo QUID , que significa 
            prescriptionDrug.prescriptionAbbreviatureCode = suggestedDosage != null && !string.IsNullOrEmpty(suggestedDosage.prescriptionAbbreviatureCode) ? suggestedDosage.prescriptionAbbreviatureCode : prescriptionAbbreviatures[0].code;

            //En caso de que el medico no quiera usar un horario predefinido, puede especificarlo manualmente
            //prescriptionDrug.hours = new List<DayTime>()
            //{
            //    new DayTime(10,0),
            //    new DayTime(18,0)
            //};

            prescriptionDrug.notes = "Prueba desde DEMO API";
            prescriptionDrug.vademecumId = environment.defaultVademecumId;
            List<PrescriptionDrug> drugsToAdd = new List<PrescriptionDrug>();
            drugsToAdd.Add(prescriptionDrug);


            Console.WriteLine("-------- Revisamos la prescripción y obtenemos una vista previa del documento de prescripción ------");
            // Hacemos una revisión de la prescripción, esto es útil para obtener una imagen del documento de la prescripción y así poder confirmar.
            // Una cita puede tener o no una prescripción, al hacer esto estamos especificando que sí tiene una prescripción.
            var encounterReview = encounterWebService.reviewEncounter(new PrescriptionDocumentRequest() { 
                encounterId = encounter.id,
                drugs = drugsToAdd
            }).Result;

            Console.WriteLine("-> URL " + encounterReview.url);

            Console.WriteLine("-------- Finalizamos la cita y enviamos la prescripción ------");
            CompleteEncounter encounterContainer = new CompleteEncounter();
            encounterContainer.encounter = encounter;
            //Registramos la ubicación geográfica del médico, ya sea obtenida por el disposivo o especificada por medio de que se seleccione un consultorio al iniciar la cita.
            // esto es importante para mantener una trazabilidad de las prescripciones por ubicación y ademas para que las farmacias cercanas puedan ver
            // esta prescripción para poder cotizar.
            encounterContainer.locationLatitude = LATITUDE;
            encounterContainer.locationLongitude = LONGITUDE;

            var finishedEncounter = encounterWebService.finishEncounterAsync(encounterContainer).Result;

            Console.WriteLine("-> ID de prescripción nueva "+finishedEncounter.prescriptionId);
            Console.WriteLine("-> Código para retirar la prescripción en farmacias " + finishedEncounter.prescriptionPublicCode);


        }

    }
}
