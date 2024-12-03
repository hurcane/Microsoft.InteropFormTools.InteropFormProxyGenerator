Imports System
Imports System.ComponentModel.Design
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.IO
Imports System.CodeDom
Imports System.CodeDom.Compiler
Imports Microsoft.VisualBasic
Imports Microsoft.VisualStudio.Shell
Imports EnvDTE
Imports EnvDTE80


''' <summary>
''' Command handler
''' </summary>
Public NotInheritable Class GenerateInteropFormProxyClasses
#Region " Template Generated Code - With mods "
    ''' <summary>
    ''' Command ID.
    ''' </summary>
    Public Const CommandId As Integer = 256

    ''' <summary>
    ''' Command menu group (command set GUID).
    ''' </summary>
    Public Shared ReadOnly CommandSet As New Guid("28d23e01-258c-44b9-9581-a4581c49aeaa")

    ''' <summary>
    ''' VS Package that provides this command, not null.
    ''' </summary>
    Private ReadOnly package As Package

    ''' <summary>
    ''' Initializes a new instance of the <see cref="GenerateInteropFormProxyClasses"/> class.
    ''' Adds our command handlers for menu (the commands must exist in the command table file)
    ''' </summary>
    ''' <param name="package">Owner package, not null.</param>
    Private Sub New(package As Package)
        If package Is Nothing Then
            Throw New ArgumentNullException("package")
        End If

        Me.package = package
        Dim commandService As OleMenuCommandService = Me.ServiceProvider.GetService(GetType(IMenuCommandService))
        If commandService IsNot Nothing Then
            Dim menuCommandId = New CommandID(CommandSet, CommandId)
            ' Using a an OLE command so we can dynamically set the text to a localized value.
            Dim menuCommand = New MenuCommand(AddressOf Me.MenuItemCallback, menuCommandId)
            commandService.AddCommand(menuCommand)
        End If

        Try
            _applicationObject = Package.GetGlobalService(GetType(DTE))
            LoadSupportedTypes()
        Catch
            DisplayError(My.Resources.LoadSupportedTypesErrMsg)
        End Try
    End Sub

    ''' <summary>
    ''' Gets the instance of the command.
    ''' </summary>
    Public Shared Property Instance As GenerateInteropFormProxyClasses

    ''' <summary>
    ''' Get service provider from the owner package.
    ''' </summary>
    Private ReadOnly Property ServiceProvider As IServiceProvider
        Get
            Return Me.package
        End Get
    End Property

    ''' <summary>
    ''' Initializes the singleton instance of the command.
    ''' </summary>
    ''' <param name="package">Owner package, Not null.</param>
    Public Shared Sub Initialize(package As Package)
        Instance = New GenerateInteropFormProxyClasses(package)
    End Sub

    ''' <summary>
    ''' This function is the callback used to execute the command when the menu item is clicked.
    ''' See the constructor to see how the menu item is associated with this function using
    ''' OleMenuCommandService service and MenuCommand class.
    ''' </summary>
    ''' <param name="sender">Event sender.</param>
    ''' <param name="e">Event args.</param>
    Private Sub MenuItemCallback(sender As Object, e As EventArgs)
        CreateInteropFormProxiesForSolution()
        If Not m_blnProxiesGenerated Then
            DisplayError(My.Resources.ADDIN_STATUS_NONE_GENERATED_OK)
        End If
    End Sub
#End Region

#Region " Private Constants "
    Private DISPLAY_CAPTION As String = My.Resources.DISPLAY_CAPTION
    Private DOCUMENT_TYPE As String = My.Resources.DOCUMENT_TYPE
    Private FOLDER_TYPE As String = My.Resources.FOLDER_TYPE
    Private INTEROP_FORM_PROXY_FOLDER_NAME As String = My.Resources.INTEROP_FORM_PROXY_FOLDER_NAME

    Private EVENT_ARGS_COMMENT As String = String.Format("{0}{1}{2}{3}{4}", My.Resources.EVENT_ARGS_COMMENT1, vbNewLine, My.Resources.EVENT_ARGS_COMMENT2, vbNewLine, My.Resources.EVENT_ARGS_COMMENT3)
#End Region

#Region " Private Variables "
    Dim _applicationObject As DTE2

    Dim _attrTypeForm As Type = GetType(InteropFormAttribute)
    Dim _attrTypeInitializer As Type = GetType(InteropFormInitializerAttribute)
    Dim _attrTypeMethod As Type = GetType(InteropFormMethodAttribute)
    Dim _attrTypeProperty As Type = GetType(InteropFormPropertyAttribute)
    Dim _attrTypeEvent As Type = GetType(InteropFormEventAttribute)

    Dim _supportedTypes As List(Of Type) = Nothing
#End Region

#Region " Private Methods "
    Private Sub LoadSupportedTypes()
        ' Load list of types that are allowed to be used in members.
        _supportedTypes = New List(Of Type) From {
            GetType(Integer),
            GetType(String),
            GetType(Boolean),
            GetType(Object)
        }
    End Sub

    Private m_blnProxiesGenerated As Boolean
    Private Sub CreateInteropFormProxiesForSolution()
        _applicationObject.StatusBar.Text = My.Resources.ADDIN_STATUS_GENERATING
        m_blnProxiesGenerated = False

        For Each assemblyProj As Project In _applicationObject.Solution.Projects
            If assemblyProj.ProjectItems IsNot Nothing AndAlso (assemblyProj.ProjectItems.Count > 0) Then
                CreateInteropFormProxiesForProject(assemblyProj, assemblyProj.ProjectItems)
            End If
        Next

        If m_blnProxiesGenerated Then
            _applicationObject.StatusBar.Text = My.Resources.ADDIN_STATUS_GENERATED_OK
        Else
            _applicationObject.StatusBar.Text = My.Resources.ADDIN_STATUS_NONE_GENERATED_OK
        End If
    End Sub

    Private Sub CreateInteropFormProxiesForProject(ByVal currentAssembly As Project, ByVal projItemCollection As ProjectItems)
        For Each projItem As ProjectItem In projItemCollection
            Try
                If projItem.Kind = DOCUMENT_TYPE AndAlso projItem.FileCodeModel IsNot Nothing Then
                    ' this is a code document so search for InteropForm classes
                    Dim interopFormClasses As List(Of CodeClass) = GetInteropFormClasses(currentAssembly, projItem)
                    ' create file of wrapper classes for the InteropForm classes found in this document
                    CreateInteropFormProxiesForDocument(interopFormClasses, currentAssembly, projItem)
                ElseIf projItem.ProjectItems IsNot Nothing AndAlso (projItem.ProjectItems.Count > 0) Then
                    ' Not a document.  It has sub items, though so search
                    CreateInteropFormProxiesForProject(currentAssembly, projItem.ProjectItems)
                End If
            Catch ex As Exception
                ' Catch here so that other projects will work and you'll know which project failed
                Dim errMsg As String = My.Resources.ADDIN_STATUS_GENERATED_ERROR1
                If currentAssembly IsNot Nothing AndAlso currentAssembly.Name IsNot Nothing Then
                    errMsg &= String.Format(My.Resources.ADDIN_STATUS_GENERATED_ERROR2, currentAssembly.Name)
                End If
                DisplayError(String.Format(My.Resources.ADDIN_STATUS_GENERATED_ERROR_FULL, currentAssembly.Name))
            End Try
        Next
    End Sub

    Private Sub CreateInteropFormProxiesForDocument(ByVal interopFormClasses As List(Of CodeClass), ByVal currentAssembly As Project, ByVal interopFormDoc As ProjectItem)
        If interopFormClasses.Count <= 0 Then
            Return
        End If

        Dim interopFormFileInfo As New FileInfo(interopFormDoc.FileNames(0))
        Dim proxyFolderInfo As New DirectoryInfo(interopFormFileInfo.DirectoryName & "\" & INTEROP_FORM_PROXY_FOLDER_NAME)

        ' check if folder is already part of the project
        Dim proxyfolderItem As ProjectItem = Nothing
        For Each level1Item As ProjectItem In currentAssembly.ProjectItems
            If level1Item.Kind = FOLDER_TYPE AndAlso level1Item.Name = INTEROP_FORM_PROXY_FOLDER_NAME Then
                proxyfolderItem = level1Item
                Exit For
            End If
        Next

        ' create folder if it doesn't already exist
        If proxyfolderItem Is Nothing Then
            If Not proxyFolderInfo.Exists Then
                'proxyFolderInfo.Create()
                proxyfolderItem = currentAssembly.ProjectItems.AddFolder(proxyFolderInfo.Name)
            Else
                ' todo: better way to add the existing folder instead of deleting first
                ' todo: fix this because it doesn't always work - item is out of synch?
                proxyFolderInfo.Delete(True)
                'proxyFolderInfo.Refresh()
                'proxyFolderInfo.Create()
                'proxyFolderInfo.Refresh()
                proxyfolderItem = currentAssembly.ProjectItems.AddFolder(proxyFolderInfo.Name)
            End If
        End If

        ' create proxy file info
        Dim proxyFilePath As String = proxyFolderInfo.FullName & "\" & interopFormFileInfo.Name.Replace(interopFormFileInfo.Extension, ".wrapper" & interopFormFileInfo.Extension)
        Dim proxyFileInfo As New FileInfo(proxyFilePath)
        Dim proxyFileItem As ProjectItem
        For Each doc As ProjectItem In proxyfolderItem.ProjectItems
            If doc.Kind = DOCUMENT_TYPE AndAlso doc.Name = proxyFileInfo.Name Then
                proxyFileItem = doc
                If currentAssembly.DTE.SourceControl.IsItemUnderSCC(proxyFilePath) Then
                    If Not doc.Collection.ContainingProject.DTE.SourceControl.IsItemCheckedOut(proxyFilePath) Then
                        doc.Collection.ContainingProject.DTE.SourceControl.CheckOutItem(proxyFilePath)
                    End If
                End If
                Exit For
            End If
        Next

        ' wipe out the old file if it exists
        If proxyFileInfo.Exists Then
            proxyFileInfo.Delete()
        End If

        Dim code As New CodeCompileUnit()
        ' Import the InteropTools namespace
        Dim nsImport As New CodeDom.CodeNamespaceImport(_attrTypeForm.Namespace)

        ' Build within that a new sub namespace called Interop
        ' So if the Form class is MyCompany.HelloWorld
        ' the proxy class is MyCompany.HelloWorld.Interop.
        ' Since the former is not exposed to COM, VB6
        ' code doesn't need to qualify the namespace so
        ' the name will look the same.
        Dim ns As New CodeDom.CodeNamespace
        ns.Name = "Interop"
        code.Namespaces.Add(ns)
        ns.Imports.Add(nsImport)

        For Each interopFormClass As CodeClass In interopFormClasses

            Dim interopFormClassName As String = interopFormClass.FullName
            Dim proxyClassName As String = interopFormClass.Name

            ' create the proxy class and add it to the namespace
            Dim proxyClass As New CodeTypeDeclaration(proxyClassName)
            ns.Types.Add(proxyClass)
            proxyClass.IsClass = True
            proxyClass.IsPartial = True
            Dim trueEx As New CodePrimitiveExpression(True)
            Dim aDual As New CodeSnippetExpression("Runtime.InteropServices.ClassInterfaceType.AutoDual")
            proxyClass.CustomAttributes.Add(New CodeAttributeDeclaration("System.Runtime.InteropServices.ClassInterface", New CodeDom.CodeAttributeArgument() {New CodeDom.CodeAttributeArgument(aDual)}))
            ' todo: is autodual right way?  Or should an explicit interface be generated?
            proxyClass.CustomAttributes.Add(New CodeAttributeDeclaration("System.Runtime.InteropServices.ComVisible", New CodeDom.CodeAttributeArgument() {New CodeDom.CodeAttributeArgument(trueEx)}))
            proxyClass.BaseTypes.Add(New CodeTypeReference(GetType(InteropFormProxyBase).Name))

            ' create the event sink interface. wait to add it to the namespace only if events exist
            Dim proxyClassEventSinkInterface As New CodeTypeDeclaration("I" & proxyClass.Name & "EventSink")
            proxyClassEventSinkInterface.CustomAttributes.Add(New CodeAttributeDeclaration("System.Runtime.InteropServices.InterfaceTypeAttribute", New CodeDom.CodeAttributeArgument() {New CodeDom.CodeAttributeArgument(New CodeSnippetExpression("System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIDispatch"))}))
            proxyClassEventSinkInterface.CustomAttributes.Add(New CodeAttributeDeclaration("System.Runtime.InteropServices.ComVisible", New CodeDom.CodeAttributeArgument() {New CodeDom.CodeAttributeArgument(trueEx)}))
            proxyClassEventSinkInterface.IsInterface = True

            Dim defaultCtor As New CodeDom.CodeConstructor()
            Dim ctorLine1 As New CodeSnippetStatement("            FormInstance = New " & interopFormClassName & "()")
            Dim ctorLine2 As New CodeSnippetStatement("            RegisterFormInstance()")

            defaultCtor.Statements.Add(ctorLine1)
            defaultCtor.Statements.Add(ctorLine2)
            defaultCtor.Attributes = MemberAttributes.Public
            proxyClass.Members.Add(defaultCtor)

            ' check the members of the interop form class for attributes
            ' and generate members in the proxy accordingly
            If interopFormClass.Members.Count > 0 Then
                For Each member As CodeElement In interopFormClass.Members
                    ' check for constructors to make Initialize methods for
                    If member.Kind = vsCMElement.vsCMElementFunction Then
                        ' cast as function object
                        Dim method As CodeFunction2 = CType(member, CodeFunction2)
                        If method.Access = vsCMAccess.vsCMAccessPublic Then
                            For Each custAtt As CodeElement In method.Attributes
                                If AttributesMatch(custAtt, _attrTypeInitializer) Then
                                    ' this method is a constructor and
                                    ' has been decorated to indicate it should
                                    ' be exposed via the proxy class
                                    AddInitializeMethodForConstructor(proxyClass, interopFormClass, method)
                                    Exit For
                                ElseIf AttributesMatch(custAtt, _attrTypeMethod) Then
                                    ' this method is a non-constructor method and
                                    ' has been decorated to indicate it should
                                    ' be exposed via the proxy class
                                    AddMethod(proxyClass, interopFormClass, method)
                                    Exit For
                                End If
                            Next
                        End If
                    ElseIf member.Kind = vsCMElement.vsCMElementProperty Then
                        ' cast as property object
                        Dim prop As CodeProperty2 = CType(member, CodeProperty2)
                        If prop.Access = vsCMAccess.vsCMAccessPublic Then

                            For Each custAtt As CodeElement In prop.Attributes
                                If AttributesMatch(custAtt, _attrTypeProperty) Then
                                    ' this method is a property and
                                    ' has been decorated to indicate it should
                                    ' be exposed via the proxy class
                                    AddProperty(proxyClass, interopFormClass, prop)
                                    Exit For
                                End If
                            Next
                        End If
                    ElseIf member.Kind = vsCMElement.vsCMElementEvent Then
                        Dim evt As CodeEvent = CType(member, CodeEvent)
                        If evt.Access = vsCMAccess.vsCMAccessPublic Then
                            For Each custAtt As CodeElement In evt.Attributes
                                If AttributesMatch(custAtt, _attrTypeEvent) Then
                                    ' this method is a property and
                                    ' has been decorated to indicate it should
                                    ' be exposed via the proxy class
                                    AddEvent(currentAssembly, proxyClass, interopFormClass, evt, proxyClassEventSinkInterface)
                                    Exit For
                                End If
                            Next
                        End If
                    End If
                Next
            End If

            ' only add the event sink if the interface was built out (i.e. the class has events)
            If proxyClassEventSinkInterface.Members.Count > 0 Then
                ns.Types.Add(proxyClassEventSinkInterface)
                proxyClass.CustomAttributes.Add(New CodeAttributeDeclaration("System.Runtime.InteropServices.ComSourceInterfaces", New CodeDom.CodeAttributeArgument() {New CodeDom.CodeAttributeArgument(New CodeDom.CodeTypeOfExpression(proxyClassEventSinkInterface.Name))}))
            End If

        Next

        Dim fsw As New System.IO.StreamWriter(proxyFileInfo.Create())
        fsw.AutoFlush = True

        Dim vb As New VBCodeProvider()

        Dim options As New CodeGeneratorOptions()
        'options("AllowLateBound") = "True"
        'options("RequireVariableDeclaration") = "True"
        'code.UserData.Add("AllowLateBound", True)
        'code.UserData.Add("RequireVariableDeclaration", True)

        vb.GenerateCodeFromCompileUnit(code, fsw, options)

        ' Close the stream
        fsw.Close()
        fsw.Dispose()

        proxyfolderItem.ProjectItems.AddFromFile(proxyFileInfo.FullName)

        'Yes, we've generated a proxy
        m_blnProxiesGenerated = True
    End Sub

    Private Function GetInteropFormClasses(ByVal assemblyProj As Project, ByVal projItem As ProjectItem) As List(Of CodeClass)
        ' Create list to hold the interopForm classes we find
        Dim interopFormClasses As New List(Of CodeClass)

        If projItem.FileCodeModel IsNot Nothing Then
            FindInteropFormClasses(assemblyProj, projItem.FileCodeModel.CodeElements, interopFormClasses)
        End If

        Return interopFormClasses
    End Function

    Private Sub FindInteropFormClasses(ByVal currentAssembly As Project, ByVal codeElements As CodeElements, ByVal interopFormClasses As List(Of CodeClass))
        ' safety check
        If codeElements Is Nothing Then
            Exit Sub
        End If

        ' todo: faster/cleaner way to find?
        For Each ce As CodeElement In codeElements
            If ce.Kind = vsCMElement.vsCMElementAttribute AndAlso AttributesMatch(ce, _attrTypeForm) Then
                ' found an InteropForm attribute so add it to the list
                Dim interopFormClass As CodeClass = CType(codeElements.Parent, CodeClass)
                interopFormClasses.Add(interopFormClass)
            End If
            If ce.Children.Count > 0 Then
                FindInteropFormClasses(currentAssembly, ce.Children, interopFormClasses)
            End If
        Next
    End Sub

    Private Sub AddInitializeMethodForConstructor(ByVal proxyClass As CodeTypeDeclaration, ByVal interopFormClass As CodeClass, ByVal method As CodeFunction)
        Dim initMethod As New CodeMemberMethod()
        initMethod.Name = "Initialize"
        initMethod.Attributes = MemberAttributes.Public
        initMethod.CustomAttributes.Add(New CodeAttributeDeclaration("System.Diagnostics.DebuggerStepThrough"))
        Dim stmt As String = "            FormInstance = New " & interopFormClass.FullName & "("
        Dim addComma As Boolean = False
        For Each pOld As CodeParameter2 In method.Parameters
            ' check against list of supported types
            If Not IsSupported(pOld.Type) Then
                DisplayWarning(String.Format(My.Resources.InitMethodErrMsg, pOld.Type.AsFullName, pOld.Name, pOld.Type.AsFullName))
                Exit Sub
            End If
            Dim pNew As New CodeParameterDeclarationExpression(pOld.Type.AsFullName, pOld.Name)
            pNew.Direction = GetParamDirection(pOld)
            initMethod.Parameters.Add(pNew)
            If addComma Then
                stmt &= ", "
            End If
            stmt &= pOld.Name
            addComma = True
        Next
        stmt &= ")"
        initMethod.Statements.Add(New CodeSnippetStatement("            UnregisterFormInstance()"))
        initMethod.Statements.Add(New CodeSnippetStatement(stmt))
        initMethod.Statements.Add(New CodeSnippetStatement("            RegisterFormInstance()"))
        proxyClass.Members.Add(initMethod)
    End Sub

    Private Sub DisplayError(ByVal errorMessage As String)
        MessageBox.Show(errorMessage, DISPLAY_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
    End Sub

    Private Sub DisplayWarning(ByVal errorMessage As String)
        MessageBox.Show(errorMessage, DISPLAY_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

    Private Function AttributesMatch(ByVal ce As CodeElement, ByVal attrType As Type) As Boolean
        Dim isMatch As Boolean = False
        Dim ceName As String = ""
        Dim staticName As String = ""

        'try matching name in CodeElement to actual type name
        'matching is case insensitive
        If ce IsNot Nothing Then
            'try matching using partial name of the class, e.g. InteropFormAttribute Or InteropForm
            If (ce.Name IsNot Nothing) AndAlso (ce.Name <> "") Then
                ceName = ce.Name
                isMatch = (ceName.ToLower = attrType.Name.ToLower) OrElse (ceName.ToLower = attrType.Name.Replace("Attribute", "").ToLower)
            End If

            'next, try matching using full name of the class, e.g. Microsoft.InteropFormsToolkit.InteropFormAttribute Or *.InteropForm
            If (isMatch = False) AndAlso (ce.FullName IsNot Nothing) AndAlso (ce.FullName <> "") Then
                ceName = ce.FullName
                isMatch = (ceName.ToLower = attrType.FullName.ToLower) OrElse (ceName.ToLower = attrType.FullName.Replace("Attribute", "").ToLower)
            End If
        End If

        Return isMatch
    End Function

    Private Sub AddMethod(ByVal proxyClass As CodeTypeDeclaration, ByVal interopFormClass As CodeClass, ByVal method As CodeFunction)
        Dim proxyMethod As New CodeMemberMethod()
        proxyMethod.Name = method.Name
        proxyMethod.Attributes = MemberAttributes.Public
        Dim trueEx As New CodePrimitiveExpression(True)
        proxyMethod.CustomAttributes.Add(New CodeAttributeDeclaration("System.Diagnostics.DebuggerStepThrough"))
        proxyMethod.Statements.Add(GetCastFormInstanceStatement(interopFormClass))
        Dim stmt As String
        If method.FunctionKind = vsCMFunction.vsCMFunctionFunction Then
            If Not IsSupported(method.Type) Then
                DisplayWarning(String.Format(My.Resources.MethodErrMsg1, method.Type.AsFullName, method.Name))
                Exit Sub
            End If

            proxyMethod.ReturnType = New CodeTypeReference(method.Type.AsFullName)
            stmt = "            Return "
        Else
            stmt = "            "
        End If
        stmt &= "castFormInstance." & method.Name & "("
        Dim addComma As Boolean = False
        For Each pOld As CodeParameter2 In method.Parameters
            ' check against list of supported types
            If Not IsSupported(pOld.Type) Then
                DisplayWarning(String.Format(My.Resources.MethodErrMsg2, pOld.Type.AsFullName, method.Name))
                Exit Sub
            End If
            Dim pNew As New CodeParameterDeclarationExpression(pOld.Type.AsFullName, pOld.Name)
            pNew.Direction = GetParamDirection(pOld)
            proxyMethod.Parameters.Add(pNew)
            If addComma Then
                stmt &= ", "
            End If
            stmt &= pOld.Name
            addComma = True
        Next
        stmt &= ")"
        proxyMethod.Statements.Add(New CodeSnippetStatement(stmt))
        proxyClass.Members.Add(proxyMethod)
    End Sub

    Private Sub AddProperty(ByVal proxyClass As CodeTypeDeclaration, ByVal interopFormClass As CodeClass, ByVal prop As CodeProperty2)
        Dim proxyProp As New CodeMemberProperty
        proxyProp.Name = prop.Name
        proxyProp.Attributes = MemberAttributes.Public
        proxyProp.Type = New CodeTypeReference(prop.Type.AsFullName)

        ' check against list of supported types
        If Not IsSupported(prop.Type) Then
            DisplayWarning(String.Format(My.Resources.PropertyErrMsg, prop.Type.AsFullName, proxyProp.Name))
            Exit Sub
        End If

        ' check for any parameters
        If prop.Parameters.Count > 0 Then
            DisplayWarning(String.Format(My.Resources.ParamPropertyErrMsg, proxyProp.Name))
            Exit Sub
        End If

        ' if there is a getter, create the getter for the proxy
        If prop.Getter IsNot Nothing Then
            proxyProp.HasGet = True
            proxyProp.GetStatements.Add(GetCastFormInstanceStatement(interopFormClass))
            proxyProp.GetStatements.Add(New CodeMethodReturnStatement(New CodePropertyReferenceExpression(New CodeVariableReferenceExpression("castFormInstance"), prop.Name)))
        End If

        ' if there is a setter, create the setter for the proxy
        If prop.Setter IsNot Nothing Then
            proxyProp.HasSet = True
            proxyProp.SetStatements.Add(GetCastFormInstanceStatement(interopFormClass))
            proxyProp.SetStatements.Add(New CodeAssignStatement(New CodePropertyReferenceExpression(New CodeVariableReferenceExpression("castFormInstance"), prop.Name), New CodePropertySetValueReferenceExpression()))
        End If

        proxyClass.Members.Add(proxyProp)
    End Sub

    Private Function GetCastFormInstanceStatement(ByVal interopFormClass As CodeClass) As CodeSnippetStatement
        Return New CodeSnippetStatement("            Dim castFormInstance As " & interopFormClass.FullName & " = FormInstance")
    End Function

    Private Sub AddEvent(ByVal currentAssembly As Project, ByVal proxyClass As CodeTypeDeclaration, ByVal interopFormClass As CodeClass, ByVal evt As CodeEvent, ByVal proxyClassEventSinkInterface As CodeTypeDeclaration)
        Dim evtDelegate As CodeDelegate2 = Nothing
        Try
            evtDelegate = CType(currentAssembly.CodeModel.CodeTypeFromFullName(evt.Type.AsFullName), CodeDelegate2)
        Catch typeFindEx As Exception
            For Each ce As CodeElement In evt.ProjectItem.FileCodeModel.CodeElements
                If ce.IsCodeType Then
                    Dim ct As CodeType = CType(ce, CodeType)
                    For Each ce2 As CodeElement In ct.Children
                        If ce2.Kind = vsCMElement.vsCMElementDelegate And ce2.FullName = evt.Type.AsFullName Then
                            evtDelegate = CType(ce2, CodeDelegate2)
                        End If
                    Next
                End If
            Next
        End Try

        If evtDelegate Is Nothing Then
            DisplayWarning(String.Format(My.Resources.EventErrMsg, evt.Name, evt.Type.AsFullName))
            Exit Sub
        End If

        ' find or create the method that hooks the event from the Form
        Dim hookCustomEventsMethod As CodeDom.CodeMemberMethod = Nothing
        For Each member As CodeTypeMember In proxyClass.Members
            If member.Name = "HookCustomEvents" Then
                hookCustomEventsMethod = CType(member, CodeMemberMethod)
            End If
        Next

        If hookCustomEventsMethod Is Nothing Then
            hookCustomEventsMethod = New CodeMemberMethod()
            hookCustomEventsMethod.Name = "HookCustomEvents"
            hookCustomEventsMethod.Attributes = MemberAttributes.Override Or MemberAttributes.Family
            hookCustomEventsMethod.Statements.Add(GetCastFormInstanceStatement(interopFormClass))
            proxyClass.Members.Add(hookCustomEventsMethod)
        End If

        ' declare the event to be added to the class
        Dim proxyEvent As New CodeDom.CodeMemberEvent()
        proxyEvent.Attributes = MemberAttributes.Public
        ' Set event type to same type as original event
        ' However, if we find System.EventArgs in the signature this will change below
        proxyEvent.Type = New CodeTypeReference(evt.Type.AsFullName)
        proxyEvent.Name = evt.Name

        ' declare the handler method to be added to the sink interface
        Dim sinkInterfaceMethod As New CodeDom.CodeMemberMethod()
        sinkInterfaceMethod.Name = evt.Name

        ' declare a new delegate for the event for the case in which
        ' the event signature includes EventArgs or a derived class
        ' and a new down-case delegate will be used instead
        ' i.e. 
        ' original delegate: xxxHandler(sender as object, e as myderivedEventArgs)
        ' new delegate:      xxxHandler(sender as object, e as System.EventArgs)
        Dim proxyDownCastDelegate As New CodeTypeDelegate(evt.Name & "Handler")
        Dim isProxyDownCastDelegateAdded As Boolean = False

        ' create the method that handles the interopform's event
        Dim proxyClassEventHandler As New CodeDom.CodeMemberMethod()
        proxyClassEventHandler.Name = "castFormInstance_" & evt.Name
        Dim reraiseEventExpression As New CodeDelegateInvokeExpression(New CodeEventReferenceExpression(New CodeThisReferenceExpression(), proxyEvent.Name))

        ' Map old parameters to new ones
        For Each pOld As CodeParameter2 In evtDelegate.Parameters
            Dim sinkInterfaceMethodParmNew As CodeParameterDeclarationExpression
            Dim proxyEventHandlerParmNew As CodeParameterDeclarationExpression
            Dim reraiseEventExpressionParmNew As CodeArgumentReferenceExpression

            ' See if paramter type is System.EventArgs or derived type.
            ' If so, expose as System.EventArgs and add comment showing caller needs
            ' to reference .NET tlb in VB6.
            If pOld.Type.CodeType IsNot Nothing AndAlso IsEventArgs(pOld.Type.CodeType) Then
                ' since we're down-casting we must create the new delegate
                If Not isProxyDownCastDelegateAdded Then
                    proxyClass.Members.Add(proxyDownCastDelegate)
                    proxyEvent.Type = New CodeTypeReference(proxyDownCastDelegate.Name)
                End If
                sinkInterfaceMethodParmNew = New CodeParameterDeclarationExpression("System.EventArgs", pOld.Name)
                sinkInterfaceMethodParmNew.Direction = GetParamDirection(pOld)
                proxyEventHandlerParmNew = New CodeParameterDeclarationExpression(pOld.Type.AsFullName, pOld.Name)
                proxyEventHandlerParmNew.Direction = GetParamDirection(pOld)
                reraiseEventExpressionParmNew = New CodeArgumentReferenceExpression(sinkInterfaceMethodParmNew.Name)

                ' add comment about the down-casting
                proxyEvent.Comments.Add(New CodeCommentStatement(EVENT_ARGS_COMMENT))
                sinkInterfaceMethod.Comments.Add(New CodeCommentStatement(EVENT_ARGS_COMMENT))
            ElseIf Not IsSupported(pOld.Type) Then
                ' else check against list of supported types
                DisplayWarning(String.Format(My.Resources.EventErrMsg2, pOld.Type.AsFullName, evt.Name))
                Exit Sub
            Else
                sinkInterfaceMethodParmNew = New CodeParameterDeclarationExpression(pOld.Type.AsFullName, pOld.Name)
                sinkInterfaceMethodParmNew.Direction = GetParamDirection(pOld)
                proxyEventHandlerParmNew = New CodeParameterDeclarationExpression(pOld.Type.AsFullName, pOld.Name)
                proxyEventHandlerParmNew.Direction = GetParamDirection(pOld)
                reraiseEventExpressionParmNew = New CodeArgumentReferenceExpression(sinkInterfaceMethodParmNew.Name)
            End If

            sinkInterfaceMethod.Parameters.Add(sinkInterfaceMethodParmNew)
            ' add same parameters to proxyDownCastDeleage as adding to 
            ' method in the sinkInterface since they have to match
            proxyDownCastDelegate.Parameters.Add(sinkInterfaceMethodParmNew)
            proxyClassEventHandler.Parameters.Add(proxyEventHandlerParmNew)
            reraiseEventExpression.Parameters.Add(reraiseEventExpressionParmNew)
        Next

        proxyClassEventHandler.Statements.Add(reraiseEventExpression)

        hookCustomEventsMethod.Statements.Add(New CodeAttachEventStatement(
                New CodeEventReferenceExpression(New CodeVariableReferenceExpression("castFormInstance"), proxyEvent.Name),
                New CodeDelegateCreateExpression(proxyEvent.Type, New CodeThisReferenceExpression(), proxyClassEventHandler.Name)))

        ' add the handler to the sink interface
        proxyClassEventSinkInterface.Members.Add(sinkInterfaceMethod)
        ' add the event to the proxy class
        proxyClass.Members.Add(proxyClassEventHandler)
        proxyClass.Members.Add(proxyEvent)
    End Sub

    'To make sure we have marked the declarations appropriately with byref or byval
    Private Function GetParamDirection(ByVal pOld As CodeParameter2) As FieldDirection
        Select Case pOld.ParameterKind
            Case vsCMParameterKind.vsCMParameterKindRef
                Return FieldDirection.Ref
            Case vsCMParameterKind.vsCMParameterKindOut
                Return FieldDirection.Out
            Case Else
                Return FieldDirection.In
        End Select
    End Function

    Private Function IsEventArgs(ByVal parmType As CodeType) As Boolean
        If parmType.FullName.ToLower() = "system.eventargs" Then
            Return True
        End If
        For Each baseElement As CodeElement In parmType.Bases
            If baseElement.FullName.ToLower() = "system.eventargs" Then
                Return True
            End If
            If baseElement.IsCodeType Then
                If IsEventArgs(CType(baseElement, CodeType)) Then
                    Return True
                End If
            End If
        Next
        Return False
    End Function

    Private Function IsSupported(ByVal typeToCheck As CodeTypeRef) As Boolean
        For Each supportedType As Type In _supportedTypes
            If typeToCheck.AsFullName = supportedType.FullName Then
                Return True
            End If
        Next
        ' wasn't in the list of supported types
        Return False
    End Function
#End Region
End Class
