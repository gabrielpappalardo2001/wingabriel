Imports System.Runtime.InteropServices
Imports System.IO

' Implementazione di una lista collegata basata su heap per la gestione efficiente dei dati.
Public Class WGList
    Implements IDisposable

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function HeapCreate(flOptions As UInteger, dwInitialSize As UIntPtr, dwMaximumSize As UIntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function HeapAlloc(hHeap As IntPtr, dwFlags As UInteger, dwBytes As UIntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function HeapFree(hHeap As IntPtr, dwFlags As UInteger, lpMem As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function HeapDestroy(hHeap As IntPtr) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure Node
        Public Data As IntPtr
        Public [Next] As IntPtr
    End Structure

    Private hHeap As IntPtr
    Private head As IntPtr
    Private disposed As Boolean = False

    Public Sub New()
        hHeap = HeapCreate(0, UIntPtr.Zero, UIntPtr.Zero)
        head = IntPtr.Zero
    End Sub

    Public Sub Aggiungi(path As String)
        Dim pPath As IntPtr = Marshal.StringToHGlobalUni(path)
        Dim nodeSize As Integer = Marshal.SizeOf(GetType(Node))
        Dim pNode As IntPtr = HeapAlloc(hHeap, 8, New UIntPtr(CUInt(nodeSize)))
        Dim newNode As New Node()
        newNode.Data = pPath
        newNode.Next = IntPtr.Zero

        If head = IntPtr.Zero Then
            Marshal.StructureToPtr(newNode, pNode, False)
            head = pNode
        Else
            Dim current As IntPtr = head
            Dim currentNode As Node
            While True
                currentNode = Marshal.PtrToStructure(Of Node)(current)
                If currentNode.Next = IntPtr.Zero Then Exit While
                current = currentNode.Next
            End While
            newNode.Data = pPath
            newNode.Next = IntPtr.Zero
            Marshal.StructureToPtr(newNode, pNode, False)
            currentNode.Next = pNode
            Marshal.StructureToPtr(currentNode, current, False)
        End If
    End Sub

    Public Function InserisciOrdinato(path As String, ordine As Integer) As String
        Dim pPath As IntPtr = Marshal.StringToHGlobalUni(path)
        Dim nodeSize As Integer = Marshal.SizeOf(GetType(Node))
        Dim pNode As IntPtr = HeapAlloc(hHeap, 8, New UIntPtr(CUInt(nodeSize)))
        Dim newNode As New Node()
        newNode.Data = pPath
        newNode.Next = IntPtr.Zero

        If head = IntPtr.Zero Then
            Marshal.StructureToPtr(newNode, pNode, False)
            head = pNode
            Return Nothing
        End If

        Dim prev As IntPtr = IntPtr.Zero
        Dim current As IntPtr = head
        Dim inserted As Boolean = False
        Dim predecessorPath As String = Nothing

        While current <> IntPtr.Zero
            Dim currentNode As Node = Marshal.PtrToStructure(Of Node)(current)
            Dim currentPath As String = Marshal.PtrToStringUni(currentNode.Data)

            If Confronta(path, currentPath, ordine) < 0 Then
                newNode.Next = current
                Marshal.StructureToPtr(newNode, pNode, False)
                If prev = IntPtr.Zero Then
                    head = pNode
                    Return Nothing
                Else
                    Dim prevNode As Node = Marshal.PtrToStructure(Of Node)(prev)
                    predecessorPath = Marshal.PtrToStringUni(prevNode.Data)
                    prevNode.Next = pNode
                    Marshal.StructureToPtr(prevNode, prev, False)
                    Return predecessorPath
                End If
                inserted = True
                Exit While
            End If

            prev = current
            current = currentNode.Next
        End While

        If Not inserted Then
            Dim prevNode As Node = Marshal.PtrToStructure(Of Node)(prev)
            predecessorPath = Marshal.PtrToStringUni(prevNode.Data)
            newNode.Next = IntPtr.Zero
            Marshal.StructureToPtr(newNode, pNode, False)
            prevNode.Next = pNode
            Marshal.StructureToPtr(prevNode, prev, False)
            Return predecessorPath
        End If

        Return Nothing
    End Function

    Private Function Confronta(path1 As String, path2 As String, ordine As Integer) As Integer
        Try
            Select Case ordine
                Case 0
                    Return String.Compare(Path.GetFileName(path1), Path.GetFileName(path2), StringComparison.OrdinalIgnoreCase)
                Case 1
                    Dim d1 = File.GetLastWriteTime(path1)
                    Dim d2 = File.GetLastWriteTime(path2)
                    Return d2.CompareTo(d1)
                Case 2
                    Dim d1 = File.GetLastWriteTime(path1)
                    Dim d2 = File.GetLastWriteTime(path2)
                    Return d1.CompareTo(d2)
                Case 3
                    Dim l1 = New FileInfo(path1).Length
                    Dim l2 = New FileInfo(path2).Length
                    Return l1.CompareTo(l2)
                Case 4
                    Dim l1 = New FileInfo(path1).Length
                    Dim l2 = New FileInfo(path2).Length
                    Return l2.CompareTo(l1)
                Case Else
                    Return String.Compare(path1, path2, StringComparison.OrdinalIgnoreCase)
            End Select
        Catch
            Return String.Compare(path1, path2, StringComparison.OrdinalIgnoreCase)
        End Try
    End Function

    Public Sub Rimuovi(path As String)
        If head = IntPtr.Zero Then Return

        Dim current As IntPtr = head
        Dim prev As IntPtr = IntPtr.Zero

        While current <> IntPtr.Zero
            Dim currentNode As Node = Marshal.PtrToStructure(Of Node)(current)
            Dim currentData As String = Marshal.PtrToStringUni(currentNode.Data)

            If currentData = path Then
                If prev = IntPtr.Zero Then
                    head = currentNode.Next
                Else
                    Dim prevNode As Node = Marshal.PtrToStructure(Of Node)(prev)
                    prevNode.Next = currentNode.Next
                    Marshal.StructureToPtr(prevNode, prev, False)
                End If
                Marshal.FreeHGlobal(currentNode.Data)
                HeapFree(hHeap, 0, current)
                Return
            End If
            prev = current
            current = currentNode.Next
        End While
    End Sub

    Public Sub Pulisci()
        Dim current As IntPtr = head
        While current <> IntPtr.Zero
            Dim currentNode As Node = Marshal.PtrToStructure(Of Node)(current)
            Dim nextPtr As IntPtr = currentNode.Next
            Marshal.FreeHGlobal(currentNode.Data)
            HeapFree(hHeap, 0, current)
            current = nextPtr
        End While
        head = IntPtr.Zero
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposed Then
            Pulisci()
            If hHeap <> IntPtr.Zero Then
                HeapDestroy(hHeap)
                hHeap = IntPtr.Zero
            End If
            disposed = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overrides Sub Finalize()
        Dispose(False)
    End Sub
End Class
