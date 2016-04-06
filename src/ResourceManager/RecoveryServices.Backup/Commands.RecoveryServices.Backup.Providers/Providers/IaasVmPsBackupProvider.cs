﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.RecoveryServices.Backup.Models;
using Microsoft.Azure.Commands.RecoveryServices.Backup.Helpers;
using Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models;
using Microsoft.Azure.Commands.RecoveryServices.Backup.Properties;
using Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.HydraAdapter;


namespace Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.ProviderModel
{
    public class IaasVmPsBackupProvider : IPsBackupProvider
    {
        private const int defaultOperationStatusRetryTimeInMilliSec = 5 * 1000; // 5 sec
        private const string separator = ";";
        private const string computeAzureVMVersion = "Compute";
        private const string classicComputeAzureVMVersion = "ClassicCompute";

        ProviderData ProviderData { get; set; }
        HydraAdapter.HydraAdapter HydraAdapter { get; set; }

        public void Initialize(ProviderData providerData, HydraAdapter.HydraAdapter hydraAdapter)
        {
            this.ProviderData = providerData;
            this.HydraAdapter = hydraAdapter;
        }

        public BaseRecoveryServicesJobResponse EnableProtection()
        {
            string azureVMName = (string)ProviderData.ProviderParameters[ItemParams.AzureVMName];
            string azureVMCloudServiceName = (string)ProviderData.ProviderParameters[ItemParams.AzureVMCloudServiceName];
            string azureVMResourceGroupName = (string)ProviderData.ProviderParameters[ItemParams.AzureVMResourceGroupName];
            string parameterSetName = (string)ProviderData.ProviderParameters[ItemParams.ParameterSetName];

            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType workloadType =
                (Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType)ProviderData.ProviderParameters[ItemParams.WorkloadType];

            AzureRmRecoveryServicesPolicyBase policy = (AzureRmRecoveryServicesPolicyBase)
                                                 ProviderData.ProviderParameters[ItemParams.Policy];

            AzureRmRecoveryServicesItemBase itemBase = (AzureRmRecoveryServicesItemBase)
                                                 ProviderData.ProviderParameters[ItemParams.Item];

            AzureRmRecoveryServicesIaasVmItem item = (AzureRmRecoveryServicesIaasVmItem)
                                                 ProviderData.ProviderParameters[ItemParams.Item];
            // do validations

            string containerUri = "";
            string protectedItemUri = "";
            bool isComputeAzureVM = false;

            if (itemBase == null)
            {
                isComputeAzureVM = string.IsNullOrEmpty(azureVMCloudServiceName) ? true : false;
                string azureVMRGName = (isComputeAzureVM) ? azureVMResourceGroupName : azureVMCloudServiceName;

                ValidateAzureVMWorkloadType(workloadType, policy.WorkloadType);

                ValidateAzureVMEnableProtectionRequest(azureVMName, azureVMCloudServiceName, azureVMResourceGroupName, policy);

                ProtectableObjectResource protectableObjectResource = GetAzureVMProtectableObject(azureVMName, azureVMRGName, isComputeAzureVM);

                Dictionary<UriEnums, string> keyValueDict = HelperUtils.ParseUri(protectableObjectResource.Id);
                containerUri = HelperUtils.GetContainerUri(keyValueDict, protectableObjectResource.Id);
                protectedItemUri = HelperUtils.GetProtectableItemUri(keyValueDict, protectableObjectResource.Id);
            }
            else
            {
                ValidateAzureVMWorkloadType(item.WorkloadType, policy.WorkloadType);
                ValidateAzureVMModifyProtectionRequest(itemBase, policy);

                isComputeAzureVM =  IsComputeAzureVM(item.VirtualMachineId);
                Dictionary<UriEnums, string> keyValueDict = HelperUtils.ParseUri(item.Id);
                containerUri = HelperUtils.GetContainerUri(keyValueDict, item.Id);
                protectedItemUri = HelperUtils.GetProtectedItemUri(keyValueDict, item.Id);
            }

            // construct Hydra protectedItem request

            AzureIaaSVMProtectedItem properties;
            if (isComputeAzureVM == false)
            {
                properties = new AzureIaaSClassicComputeVMProtectedItem();
            }
            else
            {
                properties = new AzureIaaSComputeVMProtectedItem();
            }

            properties.PolicyName = policy.Name;

            ProtectedItemCreateOrUpdateRequest hydraRequest = new ProtectedItemCreateOrUpdateRequest()
            {
                Item = new ProtectedItemResource()
                {
                    Properties = properties,
                }
            };

            return HydraAdapter.CreateOrUpdateProtectedItem(
                                containerUri,
                                protectedItemUri,
                                hydraRequest);
        }

        public BaseRecoveryServicesJobResponse DisableProtection()
        {
            bool deleteBackupData = (bool)ProviderData.ProviderParameters[ItemParams.DeleteBackupData];

            AzureRmRecoveryServicesItemBase itemBase = (AzureRmRecoveryServicesItemBase)
                                                 ProviderData.ProviderParameters[ItemParams.Item];

            AzureRmRecoveryServicesIaasVmItem item = (AzureRmRecoveryServicesIaasVmItem)
                                                 ProviderData.ProviderParameters[ItemParams.Item];
            // do validations

            ValidateAzureVMDisableProtectionRequest(itemBase);

            Dictionary<UriEnums, string> keyValueDict = HelperUtils.ParseUri(item.Id);
            string containerUri = HelperUtils.GetContainerUri(keyValueDict, item.Id);
            string protectedItemUri = HelperUtils.GetProtectedItemUri(keyValueDict, item.Id);

            bool isComputeAzureVM = false;

            if (deleteBackupData)
            {
                return HydraAdapter.DeleteProtectedItem(
                                containerUri,
                                protectedItemUri);
            }
            else
            {
                isComputeAzureVM = IsComputeAzureVM(item.VirtualMachineId);

                // construct Hydra protectedItem request

                AzureIaaSVMProtectedItem properties;
                if (isComputeAzureVM == false)
                {
                    properties = new AzureIaaSClassicComputeVMProtectedItem();
                }
                else
                {
                    properties = new AzureIaaSComputeVMProtectedItem();
                }

                properties.PolicyName = string.Empty;
                properties.ProtectionState = ItemStatus.ProtectionStopped.ToString();

                ProtectedItemCreateOrUpdateRequest hydraRequest = new ProtectedItemCreateOrUpdateRequest()
                {
                    Item = new ProtectedItemResource()
                    {
                        Properties = properties,
                    }
                };

                return HydraAdapter.CreateOrUpdateProtectedItem(
                                    containerUri,
                                    protectedItemUri,
                                    hydraRequest);
            }
        }

        public BaseRecoveryServicesJobResponse TriggerBackup()
        {
            AzureRmRecoveryServicesItemBase item = (AzureRmRecoveryServicesItemBase)ProviderData.ProviderParameters[ItemParams.Item];
            DateTime expiryDate = (DateTime)ProviderData.ProviderParameters[ItemParams.ExpiryDate];
            AzureRmRecoveryServicesIaasVmItem iaasVmItem = item as AzureRmRecoveryServicesIaasVmItem;
            return HydraAdapter.TriggerBackup(IdUtils.GetValueByName(iaasVmItem.Id, IdUtils.IdNames.ProtectionContainerName), 
                IdUtils.GetValueByName(iaasVmItem.Id, IdUtils.IdNames.ProtectedItemName));
        }

        public BaseRecoveryServicesJobResponse TriggerRestore()
        {
            AzureRmRecoveryServicesIaasVmRecoveryPoint rp = ProviderData.ProviderParameters[RestoreBackupItemParams.RecoveryPoint] 
                as AzureRmRecoveryServicesIaasVmRecoveryPoint;
            string storageId = ProviderData.ProviderParameters[RestoreBackupItemParams.StorageAccountId].ToString();

            if (rp == null)
            {
                throw new InvalidCastException("Cant convert input to AzureRmRecoveryServicesIaasVmRecoveryPoint");
            }

            var response = HydraAdapter.RestoreDisk(rp, storageId);
            return response;
        }

        public ProtectedItemResponse GetProtectedItem()
        {
            throw new NotImplementedException();
        }

        public AzureRmRecoveryServicesRecoveryPointBase GetRecoveryPointDetails()
        {
            AzureRmRecoveryServicesIaasVmItem item = ProviderData.ProviderParameters[GetRecoveryPointParams.Item]
                as AzureRmRecoveryServicesIaasVmItem;

            string recoveryPointId = ProviderData.ProviderParameters[GetRecoveryPointParams.RecoveryPointId].ToString();

            if (item == null)
            {
                throw new InvalidCastException("Cant convert input to AzureRmRecoveryServicesItemBase");
            }

            string containerName = item.ContainerName;
            string protectedItemName = (item as AzureRmRecoveryServicesIaasVmItem).Name;

            var rpResponse = HydraAdapter.GetRecoveryPointDetails(containerName, protectedItemName, recoveryPointId);
            return RecoveryPointConversions.GetPSAzureRecoveryPoints(rpResponse, item);
        }

        public List<AzureRmRecoveryServicesRecoveryPointBase> ListRecoveryPoints()
        {
            DateTime startDate = (DateTime)(ProviderData.ProviderParameters[GetRecoveryPointParams.StartDate]);
            DateTime endDate = (DateTime)(ProviderData.ProviderParameters[GetRecoveryPointParams.EndDate]);
            AzureRmRecoveryServicesIaasVmItem item = ProviderData.ProviderParameters[GetRecoveryPointParams.Item]
                as AzureRmRecoveryServicesIaasVmItem;

            if (item == null)
            {
                throw new InvalidCastException("Cant convert input to AzureRmRecoveryServicesItemBase");
            }

            string containerName = item.ContainerName;
            string protectedItemName = (item as AzureRmRecoveryServicesIaasVmItem).Name;

            TimeSpan duration = endDate - startDate;

            if (duration.TotalDays > 30)
            {
                throw new Exception("Time difference should not be more than 30 days"); //tbd: Correct nsg and exception type
            }

            //we need to fetch the list of RPs
            RecoveryPointQueryParameters queryFilter = new RecoveryPointQueryParameters();
            queryFilter.StartDate = CommonHelpers.GetDateTimeStringForService(startDate);
            queryFilter.EndDate = CommonHelpers.GetDateTimeStringForService(endDate);
            RecoveryPointListResponse rpListResponse = null;
            rpListResponse = HydraAdapter.GetRecoveryPoints(containerName, protectedItemName, queryFilter);
            return RecoveryPointConversions.GetPSAzureRecoveryPoints(rpListResponse, item);
        }

        public ProtectionPolicyResponse CreatePolicy()
        {
            string policyName = (string)ProviderData.ProviderParameters[PolicyParams.PolicyName];
            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType workloadType =
                (Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType)ProviderData.ProviderParameters[PolicyParams.WorkloadType];
            AzureRmRecoveryServicesRetentionPolicyBase retentionPolicy =
                ProviderData.ProviderParameters.ContainsKey(PolicyParams.RetentionPolicy) ?
                (AzureRmRecoveryServicesRetentionPolicyBase)ProviderData.ProviderParameters[PolicyParams.RetentionPolicy] :
                null;
            AzureRmRecoveryServicesSchedulePolicyBase schedulePolicy =
                ProviderData.ProviderParameters.ContainsKey(PolicyParams.SchedulePolicy) ?
                (AzureRmRecoveryServicesSchedulePolicyBase)ProviderData.ProviderParameters[PolicyParams.SchedulePolicy] :
                null;

            // do validations
            ValidateAzureVMWorkloadType(workloadType);
            ValidateAzureVMSchedulePolicy(schedulePolicy);
            Logger.Instance.WriteDebug("Validation of Schedule policy is successful");

            // validate RetentionPolicy
            ValidateAzureVMRetentionPolicy(retentionPolicy);
            Logger.Instance.WriteDebug("Validation of Retention policy is successful");

            // update the retention times from backupSchedule to retentionPolicy after converting to UTC           
            CopyScheduleTimeToRetentionTimes((AzureRmRecoveryServicesLongTermRetentionPolicy)retentionPolicy,
                                             (AzureRmRecoveryServicesSimpleSchedulePolicy)schedulePolicy);
            Logger.Instance.WriteDebug("Copy of RetentionTime from with SchedulePolicy to RetentionPolicy is successful");
            
            // Now validate both RetentionPolicy and SchedulePolicy together
            PolicyHelpers.ValidateLongTermRetentionPolicyWithSimpleRetentionPolicy(
                                (AzureRmRecoveryServicesLongTermRetentionPolicy)retentionPolicy,
                                (AzureRmRecoveryServicesSimpleSchedulePolicy)schedulePolicy);
            Logger.Instance.WriteDebug("Validation of Retention policy with Schedule policy is successful");

            // construct Hydra policy request            
            ProtectionPolicyRequest hydraRequest = new ProtectionPolicyRequest()
            {
                Item = new ProtectionPolicyResource()
                {
                    Properties = new AzureIaaSVMProtectionPolicy()
                    {
                        RetentionPolicy = PolicyHelpers.GetHydraLongTermRetentionPolicy(
                                                (AzureRmRecoveryServicesLongTermRetentionPolicy)retentionPolicy),
                        SchedulePolicy = PolicyHelpers.GetHydraSimpleSchedulePolicy(
                                                (AzureRmRecoveryServicesSimpleSchedulePolicy)schedulePolicy)
                    }
                }
            };

            return HydraAdapter.CreateOrUpdateProtectionPolicy(
                                 policyName,
                                 hydraRequest);
        }

        public ProtectionPolicyResponse ModifyPolicy()
        {
            AzureRmRecoveryServicesRetentionPolicyBase retentionPolicy =
               ProviderData.ProviderParameters.ContainsKey(PolicyParams.RetentionPolicy) ?
               (AzureRmRecoveryServicesRetentionPolicyBase)ProviderData.ProviderParameters[PolicyParams.RetentionPolicy] :
               null;
            AzureRmRecoveryServicesSchedulePolicyBase schedulePolicy =
                ProviderData.ProviderParameters.ContainsKey(PolicyParams.SchedulePolicy) ?
                (AzureRmRecoveryServicesSchedulePolicyBase)ProviderData.ProviderParameters[PolicyParams.SchedulePolicy] :
                null;

            AzureRmRecoveryServicesPolicyBase policy =
                ProviderData.ProviderParameters.ContainsKey(PolicyParams.ProtectionPolicy) ?
                (AzureRmRecoveryServicesPolicyBase)ProviderData.ProviderParameters[PolicyParams.ProtectionPolicy] :
                null;

            // do validations
            ValidateAzureVMProtectionPolicy(policy);
            Logger.Instance.WriteDebug("Validation of Protection Policy is successful");

            // RetentionPolicy and SchedulePolicy both should not be empty
            if (retentionPolicy == null && schedulePolicy == null)
            {
                throw new ArgumentException(Resources.BothRetentionAndSchedulePoliciesEmpty);
            }

            // validate RetentionPolicy and SchedulePolicy
            if (schedulePolicy != null)
            {
                ValidateAzureVMSchedulePolicy(schedulePolicy);
                ((AzureRmRecoveryServicesIaasVmPolicy)policy).SchedulePolicy = schedulePolicy;
                Logger.Instance.WriteDebug("Validation of Schedule policy is successful");
            }
            if (retentionPolicy != null)
            {
                ValidateAzureVMRetentionPolicy(retentionPolicy);
                ((AzureRmRecoveryServicesIaasVmPolicy)policy).RetentionPolicy = retentionPolicy;
                Logger.Instance.WriteDebug("Validation of Retention policy is successful");
            }

            // copy the backupSchedule time to retentionPolicy after converting to UTC
            CopyScheduleTimeToRetentionTimes(
                (AzureRmRecoveryServicesLongTermRetentionPolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).RetentionPolicy,
                (AzureRmRecoveryServicesSimpleSchedulePolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).SchedulePolicy);
            Logger.Instance.WriteDebug("Copy of RetentionTime from with SchedulePolicy to RetentionPolicy is successful");

            // Now validate both RetentionPolicy and SchedulePolicy matches or not
            PolicyHelpers.ValidateLongTermRetentionPolicyWithSimpleRetentionPolicy(
                (AzureRmRecoveryServicesLongTermRetentionPolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).RetentionPolicy,
                (AzureRmRecoveryServicesSimpleSchedulePolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).SchedulePolicy);
            Logger.Instance.WriteDebug("Validation of Retention policy with Schedule policy is successful");

            // construct Hydra policy request            
            ProtectionPolicyRequest hydraRequest = new ProtectionPolicyRequest()
            {
                Item = new ProtectionPolicyResource()
                {
                    Properties = new AzureIaaSVMProtectionPolicy()
                    {
                        RetentionPolicy = PolicyHelpers.GetHydraLongTermRetentionPolicy(
                                  (AzureRmRecoveryServicesLongTermRetentionPolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).RetentionPolicy),
                        SchedulePolicy = PolicyHelpers.GetHydraSimpleSchedulePolicy(
                                  (AzureRmRecoveryServicesSimpleSchedulePolicy)((AzureRmRecoveryServicesIaasVmPolicy)policy).SchedulePolicy)
                    }
                }
            };

            return HydraAdapter.CreateOrUpdateProtectionPolicy(policy.Name,
                                                               hydraRequest);            
        }

        public List<AzureRmRecoveryServicesContainerBase> ListProtectionContainers()
        {
            string name = (string)this.ProviderData.ProviderParameters[ContainerParams.Name];
            ContainerRegistrationStatus status = (ContainerRegistrationStatus)this.ProviderData.ProviderParameters[ContainerParams.Status];
            string resourceGroupName = (string)this.ProviderData.ProviderParameters[ContainerParams.ResourceGroupName];

            ProtectionContainerListQueryParams queryParams = new ProtectionContainerListQueryParams();

            // 1. Filter by Name
            queryParams.FriendlyName = name;

            // 2. Filter by ContainerType
            queryParams.ProviderType = ProviderType.AzureIaasVM.ToString();

            // 3. Filter by Status
            queryParams.RegistrationStatus = status.ToString();

            var listResponse = HydraAdapter.ListContainers(queryParams);

            List<AzureRmRecoveryServicesContainerBase> containerModels = ConversionHelpers.GetContainerModelList(listResponse);

            // 4. Filter by RG Name
            if (!string.IsNullOrEmpty(resourceGroupName))
            {
                containerModels = containerModels.Where(containerModel =>
                    (containerModel as AzureRmRecoveryServicesIaasVmContainer).ResourceGroupName == resourceGroupName).ToList();
            }

            return containerModels;
        }

        public List<AzureRmRecoveryServicesItemBase> ListProtectedItems()
        {
            AzureRmRecoveryServicesContainerBase container =
                (AzureRmRecoveryServicesContainerBase)this.ProviderData.ProviderParameters[ItemParams.Container];
            string name = (string)this.ProviderData.ProviderParameters[ItemParams.AzureVMName];
            ItemProtectionStatus protectionStatus =
                (ItemProtectionStatus)this.ProviderData.ProviderParameters[ItemParams.ProtectionStatus];
            ItemStatus status = (ItemStatus)this.ProviderData.ProviderParameters[ItemParams.Status];
            Models.WorkloadType workloadType =
                (Models.WorkloadType)this.ProviderData.ProviderParameters[ItemParams.WorkloadType];

            ProtectedItemListQueryParam queryParams = new ProtectedItemListQueryParam();
            queryParams.DatasourceType = Microsoft.Azure.Management.RecoveryServices.Backup.Models.WorkloadType.VM;
            queryParams.ProviderType = ProviderType.AzureIaasVM.ToString();

            List<ProtectedItemResource> protectedItems = new List<ProtectedItemResource>();
            string skipToken = null;
            PaginationRequest paginationRequest = null;
            do
            {
                var listResponse = HydraAdapter.ListProtectedItem(queryParams, paginationRequest);
                protectedItems.AddRange(listResponse.ItemList.Value);

                HydraHelpers.GetSkipTokenFromNextLink(listResponse.ItemList.NextLink, out skipToken);
                if (skipToken != null)
                {
                    paginationRequest = new PaginationRequest();
                    paginationRequest.SkipToken = skipToken;
                }
            } while (skipToken != null);
            
            List<AzureRmRecoveryServicesItemBase> itemModels = ConversionHelpers.GetItemModelList(protectedItems, container);

            // 1. Filter by container
            itemModels = itemModels.Where(itemModel =>
            {
                return itemModel.ContainerName == container.Name;
            }).ToList();

            // 2. Filter by item's friendly name
            if (!string.IsNullOrEmpty(name))
            {
                itemModels = itemModels.Where(itemModel =>
                {
                    return ((AzureRmRecoveryServicesIaasVmItem)itemModel).Name == name;
                }).ToList();
            }

            // 3. Filter by item's Protection Status
            if (protectionStatus != 0)
            {
                itemModels = itemModels.Where(itemModel =>
                {
                    return ((AzureRmRecoveryServicesIaasVmItem)itemModel).ProtectionStatus == protectionStatus;
                }).ToList();
            }

            // 4. Filter by item's Protection State
            if (status != 0)
            {
                itemModels = itemModels.Where(itemModel =>
                {
                    return ((AzureRmRecoveryServicesIaasVmItem)itemModel).ProtectionState == status;
                }).ToList();
            }

            // 5. Filter by workload type
            if (workloadType != 0)
            {
                itemModels = itemModels.Where(itemModel =>
                {
                    return itemModel.WorkloadType == workloadType;
                }).ToList();
            }

            return itemModels;
        }      

        public AzureRmRecoveryServicesSchedulePolicyBase GetDefaultSchedulePolicyObject()
        {
            AzureRmRecoveryServicesSimpleSchedulePolicy defaultSchedule = new AzureRmRecoveryServicesSimpleSchedulePolicy();
            //Default is daily scedule at 10:30 AM local time
            defaultSchedule.ScheduleRunFrequency = ScheduleRunType.Daily;

            DateTime scheduleTime = GenerateRandomTime();
            defaultSchedule.ScheduleRunTimes = new List<DateTime>();
            defaultSchedule.ScheduleRunTimes.Add(scheduleTime);

            defaultSchedule.ScheduleRunDays = new List<DayOfWeek>();
            defaultSchedule.ScheduleRunDays.Add(DayOfWeek.Sunday);

            return defaultSchedule;
        }

        public AzureRmRecoveryServicesRetentionPolicyBase GetDefaultRetentionPolicyObject()
        {
            AzureRmRecoveryServicesLongTermRetentionPolicy defaultRetention = new AzureRmRecoveryServicesLongTermRetentionPolicy();

            //Default time is 10:30 local time
            DateTime retentionTime = GenerateRandomTime();

            //Daily Retention policy
            defaultRetention.IsDailyScheduleEnabled = true;
            defaultRetention.DailySchedule = new Models.DailyRetentionSchedule();
            defaultRetention.DailySchedule.RetentionTimes = new List<DateTime>();
            defaultRetention.DailySchedule.RetentionTimes.Add(retentionTime);
            defaultRetention.DailySchedule.DurationCountInDays = 180; //TBD make it const

            //Weekly Retention policy
            defaultRetention.IsWeeklyScheduleEnabled = true;
            defaultRetention.WeeklySchedule = new Models.WeeklyRetentionSchedule();
            defaultRetention.WeeklySchedule.DaysOfTheWeek = new List<DayOfWeek>();
            defaultRetention.WeeklySchedule.DaysOfTheWeek.Add(DayOfWeek.Sunday);
            defaultRetention.WeeklySchedule.DurationCountInWeeks = 104; //TBD make it const
            defaultRetention.WeeklySchedule.RetentionTimes = new List<DateTime>();
            defaultRetention.WeeklySchedule.RetentionTimes.Add(retentionTime);

            //Monthly retention policy
            defaultRetention.IsMonthlyScheduleEnabled = true;
            defaultRetention.MonthlySchedule = new Models.MonthlyRetentionSchedule();
            defaultRetention.MonthlySchedule.DurationCountInMonths = 60; //tbd: make it const
            defaultRetention.MonthlySchedule.RetentionTimes = new List<DateTime>();
            defaultRetention.MonthlySchedule.RetentionTimes.Add(retentionTime);
            defaultRetention.MonthlySchedule.RetentionScheduleFormatType = Models.RetentionScheduleFormat.Weekly;

            //Initialize day based schedule
            defaultRetention.MonthlySchedule.RetentionScheduleDaily = GetDailyRetentionFormat();

            //Initialize Week based schedule
            defaultRetention.MonthlySchedule.RetentionScheduleWeekly = GetWeeklyRetentionFormat();

            //Yearly retention policy
            defaultRetention.IsYearlyScheduleEnabled = true;
            defaultRetention.YearlySchedule = new Models.YearlyRetentionSchedule();
            defaultRetention.YearlySchedule.DurationCountInYears = 10;
            defaultRetention.YearlySchedule.RetentionTimes = new List<DateTime>();
            defaultRetention.YearlySchedule.RetentionTimes.Add(retentionTime);
            defaultRetention.YearlySchedule.RetentionScheduleFormatType = Models.RetentionScheduleFormat.Weekly;
            defaultRetention.YearlySchedule.MonthsOfYear = new List<Models.Month>();
            defaultRetention.YearlySchedule.MonthsOfYear.Add(Models.Month.January);
            defaultRetention.YearlySchedule.RetentionScheduleDaily = GetDailyRetentionFormat();
            defaultRetention.YearlySchedule.RetentionScheduleWeekly = GetWeeklyRetentionFormat();
            return defaultRetention;

        }

        private static Models.DailyRetentionFormat GetDailyRetentionFormat()
        {
            Models.DailyRetentionFormat dailyRetention = new Models.DailyRetentionFormat();
            dailyRetention.DaysOfTheMonth = new List<Models.Day>();
            Models.Day dayBasedRetention = new Models.Day();
            dayBasedRetention.IsLast = false;
            dayBasedRetention.Date = 1;
            dailyRetention.DaysOfTheMonth.Add(dayBasedRetention);
            return dailyRetention;
        }

        private static Models.WeeklyRetentionFormat GetWeeklyRetentionFormat()
        {
            Models.WeeklyRetentionFormat weeklyRetention = new Models.WeeklyRetentionFormat();
            weeklyRetention.DaysOfTheWeek = new List<DayOfWeek>();
            weeklyRetention.DaysOfTheWeek.Add(DayOfWeek.Sunday);

            weeklyRetention.WeeksOfTheMonth = new List<WeekOfMonth>();
            weeklyRetention.WeeksOfTheMonth.Add(WeekOfMonth.First);
            return weeklyRetention;
        }

        private static DateTime GenerateRandomTime()
        {
            //Schedule time will be random to avoid the load in service (same is in portal as well)
            Random rand = new Random();
            int hour = rand.Next(0, 24);
            int minute = (rand.Next(0, 2) == 0) ? 0 : 30;
            return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, hour, minute, 00, DateTimeKind.Utc);
        }


        #region private
        private void ValidateAzureVMWorkloadType(Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType type)
        {
            if (type != Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType.AzureVM)
            {
                throw new ArgumentException(string.Format(Resources.UnExpectedWorkLoadTypeException,
                                            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType.AzureVM.ToString(),
                                            type.ToString()));
            }
        }

        private void ValidateAzureVMWorkloadType(Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType itemWorkloadType,
            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType policyWorkloadType)
        {
            ValidateAzureVMWorkloadType(itemWorkloadType);
            ValidateAzureVMWorkloadType(policyWorkloadType);
            if (itemWorkloadType != policyWorkloadType)
            {
                throw new ArgumentException(string.Format(Resources.UnExpectedWorkLoadTypeException,
                                            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.WorkloadType.AzureVM.ToString(),
                                            itemWorkloadType.ToString()));
            }
        }

        private void ValidateAzureVMContainerType(Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.ContainerType type)
        {
            if (type != Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.ContainerType.AzureVM)
            {
                throw new ArgumentException(string.Format(Resources.UnExpectedContainerTypeException,
                                            Microsoft.Azure.Commands.RecoveryServices.Backup.Cmdlets.Models.ContainerType.AzureVM.ToString(),
                                            type.ToString()));
            }
        }
        
        private void ValidateAzureVMProtectionPolicy(AzureRmRecoveryServicesPolicyBase policy)
        {
            if (policy == null || policy.GetType() != typeof(AzureRmRecoveryServicesIaasVmPolicy))
            {
                throw new ArgumentException(string.Format(Resources.InvalidProtectionPolicyException,
                                            typeof(AzureRmRecoveryServicesIaasVmPolicy).ToString()));
            }

            ValidateAzureVMWorkloadType(policy.WorkloadType);

            // call validation
            policy.Validate();
        }

        private void ValidateAzureVMSchedulePolicy(AzureRmRecoveryServicesSchedulePolicyBase policy)
        {
            if (policy == null || policy.GetType() != typeof(AzureRmRecoveryServicesSimpleSchedulePolicy))
            {
                throw new ArgumentException(string.Format(Resources.InvalidSchedulePolicyException,
                                            typeof(AzureRmRecoveryServicesSimpleSchedulePolicy).ToString()));
            }

            // call validation
            policy.Validate();
        }

        private void ValidateAzureVMRetentionPolicy(AzureRmRecoveryServicesRetentionPolicyBase policy)
        {
            if (policy == null || policy.GetType() != typeof(AzureRmRecoveryServicesLongTermRetentionPolicy))
            {
                throw new ArgumentException(string.Format(Resources.InvalidRetentionPolicyException,
                                            typeof(AzureRmRecoveryServicesLongTermRetentionPolicy).ToString()));
            }

            // call validation
            policy.Validate();
        }

        private void ValidateAzureVMEnableProtectionRequest(string vmName, string serviceName, string rgName,
            AzureRmRecoveryServicesPolicyBase policy)
        {
            if (string.IsNullOrEmpty(vmName))
            {
                throw new ArgumentException(string.Format(Resources.InvalidAzureVMName));
            }
            if (string.IsNullOrEmpty(rgName) && string.IsNullOrEmpty(serviceName))
            {
                throw new ArgumentException(string.Format(Resources.BothCloudServiceNameAndResourceGroupNameShouldNotEmpty));
            }
        }

        private void ValidateAzureVMModifyProtectionRequest(AzureRmRecoveryServicesItemBase itemBase,
            AzureRmRecoveryServicesPolicyBase policy)
        {
            if (itemBase == null || itemBase.GetType() != typeof(AzureRmRecoveryServicesIaasVmItem))
            {
                throw new ArgumentException(string.Format(Resources.InvalidProtectionPolicyException,
                                            typeof(AzureRmRecoveryServicesIaasVmItem).ToString()));
            }

            if(string.IsNullOrEmpty(((AzureRmRecoveryServicesIaasVmItem)itemBase).VirtualMachineId))
            {
                throw new ArgumentException(Resources.VirtualMachineIdIsEmptyOrNull);
            }
        }

        private void ValidateAzureVMDisableProtectionRequest(AzureRmRecoveryServicesItemBase itemBase)
        {

            if (itemBase == null || itemBase.GetType() != typeof(AzureRmRecoveryServicesIaasVmItem))
            {
                throw new ArgumentException(string.Format(Resources.InvalidProtectionPolicyException,
                                            typeof(AzureRmRecoveryServicesIaasVmItem).ToString()));
            }

            if (string.IsNullOrEmpty(((AzureRmRecoveryServicesIaasVmItem)itemBase).VirtualMachineId))
            {
                throw new ArgumentException(Resources.VirtualMachineIdIsEmptyOrNull);
            }

            ValidateAzureVMWorkloadType(itemBase.WorkloadType);
            ValidateAzureVMContainerType(itemBase.ContainerType);
        }

        private bool IsComputeAzureVM(string virtualMachineId)
        {
            bool isComputeAzureVM = true;
            if (virtualMachineId.IndexOf(classicComputeAzureVMVersion, StringComparison.InvariantCultureIgnoreCase) >= 0)
            {
                isComputeAzureVM = false;
            }
            return isComputeAzureVM;
        }

        private ProtectableObjectResource GetAzureVMProtectableObject(string azureVMName, string azureVMRGName, bool isComputeAzureVM)
        {
            //TriggerDiscovery if needed

            bool isDiscoveryNeed = false;

            ProtectableObjectResource protectableObjectResource = null;
            isDiscoveryNeed = IsDiscoveryNeeded(azureVMName, azureVMRGName, isComputeAzureVM, out protectableObjectResource);
            if (isDiscoveryNeed)
            {
                Logger.Instance.WriteDebug(String.Format(Resources.VMNotDiscovered, azureVMName));
                RefreshContainer();
                isDiscoveryNeed = IsDiscoveryNeeded(azureVMName, azureVMRGName, isComputeAzureVM, out protectableObjectResource);
                if (isDiscoveryNeed == true)
                {
                    // Container is not discovered. Throw exception
                    string vmversion = (isComputeAzureVM) ? computeAzureVMVersion : classicComputeAzureVMVersion;
                    string errMsg = String.Format(Resources.DiscoveryFailure, azureVMName, azureVMRGName, vmversion);
                    Logger.Instance.WriteDebug(errMsg);
                    Logger.Instance.ThrowTerminatingError(new ErrorRecord(new Exception(Resources.AzureVMNotFound), string.Empty, ErrorCategory.InvalidArgument, null));
                }
            }
            if (protectableObjectResource == null)
            {
                // Container is not discovered. Throw exception
                string vmversion = (isComputeAzureVM) ? computeAzureVMVersion : classicComputeAzureVMVersion;
                string errMsg = String.Format(Resources.DiscoveryFailure, azureVMName, azureVMRGName, vmversion);
                Logger.Instance.WriteDebug(errMsg);
                Logger.Instance.ThrowTerminatingError(new ErrorRecord(new Exception(Resources.AzureVMNotFound), string.Empty, ErrorCategory.InvalidArgument, null));
            }

            return protectableObjectResource;

        }

        private bool IsDiscoveryNeeded(string vmName, string rgName, bool isComputeAzureVM,
            out ProtectableObjectResource protectableObjectResource)
        {
            bool isDiscoveryNeed = true;
            protectableObjectResource = null;
            string vmVersion = string.Empty;
            vmVersion = (isComputeAzureVM) == true ? computeAzureVMVersion : classicComputeAzureVMVersion;

            ProtectableObjectListQueryParameters queryParam = new ProtectableObjectListQueryParameters();
            // --- TBD To be added once bug is fixed in hydra and service
            //queryParam.ProviderType = ProviderType.AzureIaasVM.ToString();
            //queryParam.FriendlyName = vmName;

            // No need to use skip or top token here as no pagination support of IaaSVM PO.

            //First check if container is discovered or not
            var protectableItemList = HydraAdapter.ListProtectableItem(queryParam).ItemList;

            Logger.Instance.WriteDebug(String.Format(Resources.ContainerCountAfterFilter, protectableItemList.ProtectableObjects.Count()));
            if (protectableItemList.ProtectableObjects.Count() == 0)
            {
                //Container is not discovered
                Logger.Instance.WriteDebug(Resources.ContainerNotDiscovered);
                isDiscoveryNeed = true;
            }
            else
            {
                foreach (var protectableItem in protectableItemList.ProtectableObjects)
                {
                    AzureIaaSVMProtectableItem iaaSVMProtectableItem = (AzureIaaSVMProtectableItem)protectableItem.Properties;
                    if (iaaSVMProtectableItem != null &&
                        string.Compare(iaaSVMProtectableItem.FriendlyName, vmName, true) == 0
                        && string.Compare(iaaSVMProtectableItem.ResourceGroup, rgName, true) == 0
                        && string.Compare(iaaSVMProtectableItem.VirtualMachineVersion, vmVersion, true) == 0)
                    {
                        protectableObjectResource = protectableItem;
                        isDiscoveryNeed = false;
                        break;
                    }
                }
            }

            return isDiscoveryNeed;
        }

        private void RefreshContainer()
        {
            bool isDiscoverySuccessful = false;
            string errorMessage = string.Empty;
            var refreshContainerJobResponse = HydraAdapter.RefreshContainers();

            //Now wait for the operation to Complete
            WaitForDiscoveryToComplete(refreshContainerJobResponse.Location, out isDiscoverySuccessful, out errorMessage);

            if (!isDiscoverySuccessful)
            {
                Logger.Instance.ThrowTerminatingError(new ErrorRecord(new Exception(errorMessage), string.Empty, ErrorCategory.InvalidArgument, null));
            }
        }

        private void WaitForDiscoveryToComplete(string locationUri, out bool isDiscoverySuccessful, out string errorMessage)
        {
            var status = TrackRefreshContainerOperation(locationUri);
            errorMessage = String.Empty;

            isDiscoverySuccessful = true;
            //If operation fails check if retry is needed or not
            if (status != HttpStatusCode.NoContent)
            {
                isDiscoverySuccessful = false;
                errorMessage = String.Format(Resources.DiscoveryFailureErrorCode, status);
                Logger.Instance.WriteDebug(errorMessage);
            }
        }

        private HttpStatusCode TrackRefreshContainerOperation(string operationResultLink, int checkFrequency = defaultOperationStatusRetryTimeInMilliSec)
        {
            HttpStatusCode status = HttpStatusCode.Accepted;
            while (status == HttpStatusCode.Accepted)
            {
                try
                {
                    var response = HydraAdapter.GetRefreshContainerOperationResultByURL(operationResultLink);
                    status = response.StatusCode;

                    Thread.Sleep(checkFrequency);
                }
                catch (Exception ex)
                {
                    Logger.Instance.WriteDebug(ex.Message);
                    status = HttpStatusCode.InternalServerError;
                    break;
                }
            }

            if (status == HttpStatusCode.NoContent)
            {
                Logger.Instance.WriteDebug("Refresh Container Job completed with success");
            }
            else
            {
                string msg = String.Format("Unexpected http status in response header {0}", status);
                Logger.Instance.WriteDebug(msg);
                throw new Exception(msg);
            }

            return status;
        }

        private void CopyScheduleTimeToRetentionTimes(AzureRmRecoveryServicesLongTermRetentionPolicy retPolicy,
                                                      AzureRmRecoveryServicesSimpleSchedulePolicy schPolicy)
        {
            // schedule runTimes is already validated if in UTC/not during validate()
            // now copy times from schedule to retention policy
            if (retPolicy.IsDailyScheduleEnabled && retPolicy.DailySchedule != null)
            {
                retPolicy.DailySchedule.RetentionTimes = schPolicy.ScheduleRunTimes;
            }

            if (retPolicy.IsWeeklyScheduleEnabled && retPolicy.WeeklySchedule != null)
            {
                retPolicy.WeeklySchedule.RetentionTimes = schPolicy.ScheduleRunTimes;
            }

            if (retPolicy.IsMonthlyScheduleEnabled && retPolicy.MonthlySchedule != null)
            {
                retPolicy.MonthlySchedule.RetentionTimes = schPolicy.ScheduleRunTimes;
            }

            if (retPolicy.IsYearlyScheduleEnabled && retPolicy.YearlySchedule != null)
            {
                retPolicy.YearlySchedule.RetentionTimes = schPolicy.ScheduleRunTimes;
            }
        }

        #endregion
    }
}
